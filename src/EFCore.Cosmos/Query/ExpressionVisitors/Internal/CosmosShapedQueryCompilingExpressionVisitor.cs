﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Cosmos.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Cosmos.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Cosmos.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Cosmos.Query.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.Pipeline;
using Microsoft.EntityFrameworkCore.Storage;
using Newtonsoft.Json.Linq;

namespace Microsoft.EntityFrameworkCore.Cosmos.Query.ExpressionVisitors.Internal
{
    public class CosmosShapedQueryCompilingExpressionVisitor : ShapedQueryCompilingExpressionVisitor
    {
        private readonly ISqlExpressionFactory _sqlExpressionFactory;
        private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
        private readonly Type _contextType;
        private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

        public CosmosShapedQueryCompilingExpressionVisitor(
            IEntityMaterializerSource entityMaterializerSource,
            ISqlExpressionFactory sqlExpressionFactory,
            IQuerySqlGeneratorFactory querySqlGeneratorFactory,
            Type contextType,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger,
            bool trackQueryResults,
            bool async)
            : base(entityMaterializerSource, trackQueryResults, async)
        {
            _sqlExpressionFactory = sqlExpressionFactory;
            _querySqlGeneratorFactory = querySqlGeneratorFactory;
            _contextType = contextType;
            _logger = logger;
        }

        protected override Expression VisitShapedQueryExpression(ShapedQueryExpression shapedQueryExpression)
        {
            var selectExpression = (SelectExpression)shapedQueryExpression.QueryExpression;
            selectExpression.ApplyProjection();

            var jObjectParameter = Expression.Parameter(typeof(JObject), "jObject");
            var shaperBody = new CosmosProjectionBindingRemovingExpressionVisitor(selectExpression, jObjectParameter, this)
                .Visit(shapedQueryExpression.ShaperExpression);

            var shaperLambda = Expression.Lambda(
                shaperBody,
                QueryCompilationContext.QueryContextParameter,
                jObjectParameter);

            return Expression.New(
                (Async
                    ? typeof(AsyncQueryingEnumerable<>)
                    : typeof(QueryingEnumerable<>)).MakeGenericType(shaperLambda.ReturnType).GetConstructors()[0],
                Expression.Convert(QueryCompilationContext.QueryContextParameter, typeof(CosmosQueryContext)),
                Expression.Constant(_sqlExpressionFactory),
                Expression.Constant(_querySqlGeneratorFactory),
                Expression.Constant(selectExpression),
                Expression.Constant(shaperLambda.Compile()),
                Expression.Constant(_contextType),
                Expression.Constant(_logger));
        }

        private class CosmosProjectionBindingRemovingExpressionVisitor : ExpressionVisitor
        {
            private static readonly MethodInfo _getItemMethodInfo
                = typeof(JObject).GetTypeInfo().GetRuntimeProperties()
                    .Single(pi => pi.Name == "Item" && pi.GetIndexParameters()[0].ParameterType == typeof(string))
                    .GetMethod;
            private static readonly MethodInfo _toObjectMethodInfo
                = typeof(CosmosProjectionBindingRemovingExpressionVisitor).GetTypeInfo().GetRuntimeMethods()
                    .Single(mi => mi.Name == nameof(SafeToObject));
            private static readonly MethodInfo _isNullMethodInfo
                = typeof(CosmosProjectionBindingRemovingExpressionVisitor).GetTypeInfo().GetRuntimeMethods()
                    .Single(mi => mi.Name == nameof(IsNull));

            private readonly SelectExpression _selectExpression;
            private ParameterExpression _jObjectParameter;
            private readonly CosmosShapedQueryCompilingExpressionVisitor _shapedQueryCompilingExpressionVisitor;

            private readonly IDictionary<ProjectionBindingExpression, Expression> _valueBufferBindings
                = new Dictionary<ProjectionBindingExpression, Expression>();
            private readonly IDictionary<ParameterExpression, Expression> _materializationContextBindings
                = new Dictionary<ParameterExpression, Expression>();
            private int _currentEntityIndex;

            public CosmosProjectionBindingRemovingExpressionVisitor(
                SelectExpression selectExpression,
                ParameterExpression jObjectParameter,
                CosmosShapedQueryCompilingExpressionVisitor shapedQueryCompilingExpressionVisitor)
            {
                _selectExpression = selectExpression;
                _jObjectParameter = jObjectParameter;
                _shapedQueryCompilingExpressionVisitor = shapedQueryCompilingExpressionVisitor;
            }

            protected override Expression VisitBinary(BinaryExpression binaryExpression)
            {
                if (binaryExpression.NodeType == ExpressionType.Assign
                    && binaryExpression.Left is ParameterExpression parameterExpression
                    && parameterExpression.Type == typeof(MaterializationContext))
                {
                    var newExpression = (NewExpression)binaryExpression.Right;

                    _materializationContextBindings[parameterExpression] = _jObjectParameter;

                    var updatedExpression = Expression.New(newExpression.Constructor,
                        Expression.Constant(ValueBuffer.Empty),
                        newExpression.Arguments[1]);

                    return Expression.MakeBinary(ExpressionType.Assign, binaryExpression.Left, updatedExpression);
                }

                if (binaryExpression.NodeType == ExpressionType.Assign
                    && binaryExpression.Left is MemberExpression memberExpression
                    && memberExpression.Member is FieldInfo fieldInfo
                    && fieldInfo.IsInitOnly)
                {
                    return memberExpression.Assign(Visit(binaryExpression.Right));
                }

                return base.VisitBinary(binaryExpression);
            }

            protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
            {
                if (methodCallExpression.Method.IsGenericMethod
                    && methodCallExpression.Method.GetGenericMethodDefinition() == EntityMaterializerSource.TryReadValueMethod)
                {
                    var property = (IProperty)((ConstantExpression)methodCallExpression.Arguments[2]).Value;
                    Expression innerExpression;
                    if (methodCallExpression.Arguments[0] is ProjectionBindingExpression projectionBindingExpression)
                    {
                        var projectionIndex = (int)GetProjectionIndex(projectionBindingExpression);
                        var projection = _selectExpression.Projection[projectionIndex];

                        innerExpression = Expression.Convert(
                            CreateReadJTokenExpression(_jObjectParameter, projection.Alias),
                            typeof(JObject));
                    }
                    else
                    {
                        innerExpression = _materializationContextBindings[
                            (ParameterExpression)((MethodCallExpression)methodCallExpression.Arguments[0]).Object];
                    }

                    var readExpression = CreateGetValueExpression(innerExpression, property);
                    if (readExpression.Type.IsValueType
                        && methodCallExpression.Type == typeof(object))
                    {
                        readExpression = Expression.Convert(readExpression, typeof(object));
                    }

                    return readExpression;
                }

                return base.VisitMethodCall(methodCallExpression);
            }

            protected override Expression VisitExtension(Expression extensionExpression)
            {
                if (extensionExpression is ProjectionBindingExpression projectionBindingExpression)
                {
                    var projectionIndex = (int)GetProjectionIndex(projectionBindingExpression);
                    var projection = _selectExpression.Projection[projectionIndex];

                    return CreateGetStoreValueExpression(
                        _jObjectParameter,
                        projection.Alias,
                        ((SqlExpression)projection.Expression).TypeMapping,
                        projectionBindingExpression.Type);
                }

                if (extensionExpression is EntityShaperExpression shaperExpression)
                {
                    _currentEntityIndex++;

                    var jObjectVariable = Expression.Variable(typeof(JObject),
                        "jObject" + _currentEntityIndex);
                    var variables = new List<ParameterExpression> { jObjectVariable };

                    var expressions = new List<Expression>();

                    if (shaperExpression.ParentNavigation == null)
                    {
                        var projectionIndex = (int)GetProjectionIndex((ProjectionBindingExpression)shaperExpression.ValueBufferExpression);
                        var projection = _selectExpression.Projection[projectionIndex];

                        expressions.Add(
                            Expression.Assign(
                                jObjectVariable,
                                Expression.TypeAs(
                                    CreateReadJTokenExpression(_jObjectParameter, projection.Alias),
                                    typeof(JObject))));

                        shaperExpression = shaperExpression.Update(
                            shaperExpression.ValueBufferExpression,
                            GetNestedShapers(shaperExpression.EntityType, shaperExpression.ValueBufferExpression));
                    }
                    else
                    {
                        var methodCallExpression = (MethodCallExpression)shaperExpression.ValueBufferExpression;
                        Debug.Assert(methodCallExpression.Method.IsGenericMethod
                            && methodCallExpression.Method.GetGenericMethodDefinition() == EntityMaterializerSource.TryReadValueMethod);

                        var navigation = (INavigation)((ConstantExpression)methodCallExpression.Arguments[2]).Value;

                        expressions.Add(
                            Expression.Assign(
                                jObjectVariable,
                                Expression.TypeAs(
                                    CreateReadJTokenExpression(_jObjectParameter, navigation.GetTargetType().GetCosmosContainingPropertyName()),
                                    typeof(JObject))));
                    }

                    var parentJObject = _jObjectParameter;
                    _jObjectParameter = jObjectVariable;
                    expressions.Add(Expression.Condition(
                        Expression.Equal(jObjectVariable, Expression.Constant(null, jObjectVariable.Type)),
                        Expression.Constant(null, shaperExpression.Type),
                        Visit(_shapedQueryCompilingExpressionVisitor.InjectEntityMaterializer(shaperExpression))));
                    _jObjectParameter = parentJObject;

                    return Expression.Block(
                        shaperExpression.Type,
                        variables,
                        expressions);
                }

                if (extensionExpression is CollectionShaperExpression collectionShaperExpression)
                {
                    throw new NotImplementedException();
                }

                return base.VisitExtension(extensionExpression);
            }

            private List<EntityShaperExpression> GetNestedShapers(IEntityType entityType, Expression valueBufferExpression)
            {
                var nestedEntities = new List<EntityShaperExpression>();
                foreach (var ownedNavigation in entityType.GetNavigations().Concat(entityType.GetDerivedNavigations()))
                {
                    var fk = ownedNavigation.ForeignKey;
                    if (!fk.IsOwnership
                        || ownedNavigation.IsDependentToPrincipal()
                        || fk.DeclaringEntityType.IsDocumentRoot())
                    {
                        continue;
                    }

                    var targetType = ownedNavigation.GetTargetType();

                    var nestedValueBufferExpression = _shapedQueryCompilingExpressionVisitor.CreateReadValueExpression(
                                valueBufferExpression,
                                valueBufferExpression.Type,
                                ownedNavigation.GetIndex(),
                                ownedNavigation);

                    var nestedShaper = new EntityShaperExpression(
                        targetType, nestedValueBufferExpression, nullable: true, ownedNavigation,
                        GetNestedShapers(targetType, nestedValueBufferExpression));
                    nestedEntities.Add(nestedShaper);
                }

                return nestedEntities.Count == 0 ? null : nestedEntities;
            }

            private object GetProjectionIndex(ProjectionBindingExpression projectionBindingExpression)
            {
                return projectionBindingExpression.ProjectionMember != null
                    ? ((ConstantExpression)_selectExpression.GetMappedProjection(projectionBindingExpression.ProjectionMember)).Value
                    : (projectionBindingExpression.Index != null
                        ? projectionBindingExpression.Index
                        : throw new InvalidOperationException());
            }

            private static Expression CreateReadJTokenExpression(Expression jObjectExpression, string propertyName)
            {
                return Expression.Call(jObjectExpression, _getItemMethodInfo, Expression.Constant(propertyName));
            }

            private static Expression CreateGetValueExpression(
                Expression jObjectExpression,
                IProperty property)
            {
                if (property.Name == StoreKeyConvention.JObjectPropertyName)
                {
                    return jObjectExpression;
                }

                var storeName = property.GetCosmosPropertyName();
                if (storeName.Length == 0)
                {
                    return Expression.Default(property.ClrType);
                }

                return CreateGetStoreValueExpression(jObjectExpression, storeName, property.GetTypeMapping(), property.ClrType);
            }

            private static Expression CreateGetStoreValueExpression(
                Expression jObjectExpression,
                string storeName,
                CoreTypeMapping typeMapping,
                Type clrType)
            {
                var jTokenExpression = Expression.Call(jObjectExpression, _getItemMethodInfo, Expression.Constant(storeName));
                Expression valueExpression;

                var converter = typeMapping.Converter;
                if (converter != null)
                {
                    valueExpression = ConvertJTokenToType(jTokenExpression, converter.ProviderClrType);

                    valueExpression = ReplacingExpressionVisitor.Replace(
                        converter.ConvertFromProviderExpression.Parameters.Single(),
                        valueExpression,
                        converter.ConvertFromProviderExpression.Body);

                    if (valueExpression.Type != clrType)
                    {
                        valueExpression = Expression.Convert(valueExpression, clrType);
                    }
                }
                else
                {
                    valueExpression = ConvertJTokenToType(jTokenExpression, clrType);
                }

                if (clrType.IsNullableType())
                {
                    valueExpression =
                        Expression.Condition(
                            Expression.Call(_isNullMethodInfo, jTokenExpression),
                            Expression.Default(valueExpression.Type),
                            valueExpression);
                }

                return valueExpression;
            }

            private static Expression ConvertJTokenToType(Expression jTokenExpression, Type type)
            {
                return Expression.Call(
                    _toObjectMethodInfo.MakeGenericMethod(type),
                    jTokenExpression);
            }

            private static T SafeToObject<T>(JToken token)
                => token == null ? default : token.ToObject<T>();

            private static bool IsNull(JToken token)
                => token == null || token.Type == JTokenType.Null;
        }

        private class QueryingEnumerable<T> : IEnumerable<T>
        {
            private readonly CosmosQueryContext _cosmosQueryContext;
            private readonly ISqlExpressionFactory _sqlExpressionFactory;
            private readonly SelectExpression _selectExpression;
            private readonly Func<QueryContext, JObject, T> _shaper;
            private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
            private readonly Type _contextType;
            private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

            public QueryingEnumerable(
                CosmosQueryContext cosmosQueryContext,
                ISqlExpressionFactory sqlExpressionFactory,
                IQuerySqlGeneratorFactory querySqlGeneratorFactory,
                SelectExpression selectExpression,
                Func<QueryContext, JObject, T> shaper,
                Type contextType,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            {
                _cosmosQueryContext = cosmosQueryContext;
                _sqlExpressionFactory = sqlExpressionFactory;
                _querySqlGeneratorFactory = querySqlGeneratorFactory;
                _selectExpression = selectExpression;
                _shaper = shaper;
                _contextType = contextType;
                _logger = logger;
            }

            public IEnumerator<T> GetEnumerator() => new Enumerator(this);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator : IEnumerator<T>
            {
                private IEnumerator<JObject> _enumerator;
                private readonly CosmosQueryContext _cosmosQueryContext;
                private readonly SelectExpression _selectExpression;
                private readonly Func<QueryContext, JObject, T> _shaper;
                private readonly ISqlExpressionFactory _sqlExpressionFactory;
                private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
                private readonly Type _contextType;
                private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

                public Enumerator(QueryingEnumerable<T> queryingEnumerable)
                {
                    _cosmosQueryContext = queryingEnumerable._cosmosQueryContext;
                    _shaper = queryingEnumerable._shaper;
                    _selectExpression = queryingEnumerable._selectExpression;
                    _sqlExpressionFactory = queryingEnumerable._sqlExpressionFactory;
                    _querySqlGeneratorFactory = queryingEnumerable._querySqlGeneratorFactory;
                    _contextType = queryingEnumerable._contextType;
                    _logger = queryingEnumerable._logger;
                }

                public T Current { get; private set; }

                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    try
                    {
                        if (_enumerator == null)
                        {
                            var selectExpression = (SelectExpression)new InExpressionValuesExpandingExpressionVisitor(
                                _sqlExpressionFactory, _cosmosQueryContext.ParameterValues).Visit(_selectExpression);

                            var sqlQuery = _querySqlGeneratorFactory.Create().GetSqlQuery(
                                selectExpression, _cosmosQueryContext.ParameterValues);

                            _enumerator = _cosmosQueryContext.CosmosClient
                                .ExecuteSqlQuery(
                                    _selectExpression.ContainerName,
                                    sqlQuery)
                                .GetEnumerator();
                        }

                        var hasNext = _enumerator.MoveNext();

                        Current
                            = hasNext
                                ? _shaper(_cosmosQueryContext, _enumerator.Current)
                                : default;

                        return hasNext;
                    }
                    catch (Exception exception)
                    {
                        _logger.QueryIterationFailed(_contextType, exception);

                        throw;
                    }
                }

                public void Dispose()
                {
                    _enumerator?.Dispose();
                    _enumerator = null;
                }

                public void Reset() => throw new NotImplementedException();
            }
        }

        private class AsyncQueryingEnumerable<T> : IAsyncEnumerable<T>
        {
            private readonly CosmosQueryContext _cosmosQueryContext;
            private readonly SelectExpression _selectExpression;
            private readonly Func<QueryContext, JObject, T> _shaper;
            private readonly ISqlExpressionFactory _sqlExpressionFactory;
            private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
            private readonly Type _contextType;
            private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;

            public AsyncQueryingEnumerable(
                CosmosQueryContext cosmosQueryContext,
                ISqlExpressionFactory sqlExpressionFactory,
                IQuerySqlGeneratorFactory querySqlGeneratorFactory,
                SelectExpression selectExpression,
                Func<QueryContext, JObject, T> shaper,
                Type contextType,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            {
                _cosmosQueryContext = cosmosQueryContext;
                _sqlExpressionFactory = sqlExpressionFactory;
                _querySqlGeneratorFactory = querySqlGeneratorFactory;
                _selectExpression = selectExpression;
                _shaper = shaper;
                _contextType = contextType;
                _logger = logger;
            }

            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new AsyncEnumerator(this, cancellationToken);
            }

            private sealed class AsyncEnumerator : IAsyncEnumerator<T>
            {
                private IAsyncEnumerator<JObject> _enumerator;
                private readonly CosmosQueryContext _cosmosQueryContext;
                private readonly SelectExpression _selectExpression;
                private readonly Func<QueryContext, JObject, T> _shaper;
                private readonly ISqlExpressionFactory _sqlExpressionFactory;
                private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
                private readonly Type _contextType;
                private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;
                private readonly CancellationToken _cancellationToken;

                public AsyncEnumerator(AsyncQueryingEnumerable<T> queryingEnumerable, CancellationToken cancellationToken)
                {
                    _cosmosQueryContext = queryingEnumerable._cosmosQueryContext;
                    _shaper = queryingEnumerable._shaper;
                    _selectExpression = queryingEnumerable._selectExpression;
                    _sqlExpressionFactory = queryingEnumerable._sqlExpressionFactory;
                    _querySqlGeneratorFactory = queryingEnumerable._querySqlGeneratorFactory;
                    _contextType = queryingEnumerable._contextType;
                    _logger = queryingEnumerable._logger;
                    _cancellationToken = cancellationToken;
                }

                public T Current { get; private set; }

                public async ValueTask<bool> MoveNextAsync()
                {
                    try
                    {
                        if (_enumerator == null)
                        {
                            var selectExpression = (SelectExpression)new InExpressionValuesExpandingExpressionVisitor(
                               _sqlExpressionFactory, _cosmosQueryContext.ParameterValues).Visit(_selectExpression);

                            _enumerator = _cosmosQueryContext.CosmosClient
                                .ExecuteSqlQueryAsync(
                                    _selectExpression.ContainerName,
                                    _querySqlGeneratorFactory.Create().GetSqlQuery(selectExpression, _cosmosQueryContext.ParameterValues))
                                .GetAsyncEnumerator(_cancellationToken);

                        }

                        var hasNext = await _enumerator.MoveNextAsync();

                        Current
                            = hasNext
                                ? _shaper(_cosmosQueryContext, _enumerator.Current)
                                : default;

                        return hasNext;
                    }
                    catch (Exception exception)
                    {
                        _logger.QueryIterationFailed(_contextType, exception);

                        throw;
                    }
                }

                public ValueTask DisposeAsync()
                {
                    _enumerator?.DisposeAsync();
                    _enumerator = null;

                    return default;
                }
            }
        }

        private class InExpressionValuesExpandingExpressionVisitor : ExpressionVisitor
        {
            private readonly ISqlExpressionFactory _sqlExpressionFactory;
            private IReadOnlyDictionary<string, object> _parametersValues;

            public InExpressionValuesExpandingExpressionVisitor(
                ISqlExpressionFactory sqlExpressionFactory, IReadOnlyDictionary<string, object> parametersValues)
            {
                _sqlExpressionFactory = sqlExpressionFactory;
                _parametersValues = parametersValues;
            }

            public override Expression Visit(Expression expression)
            {
                if (expression is InExpression inExpression)
                {
                    var inValues = new List<object>();
                    var hasNullValue = false;
                    CoreTypeMapping typeMapping = null;

                    switch (inExpression.Values)
                    {
                        case SqlConstantExpression sqlConstant:
                            {
                                typeMapping = sqlConstant.TypeMapping;
                                var values = (IEnumerable)sqlConstant.Value;
                                foreach (var value in values)
                                {
                                    if (value == null)
                                    {
                                        hasNullValue = true;
                                        continue;
                                    }

                                    inValues.Add(value);
                                }
                            }
                            break;

                        case SqlParameterExpression sqlParameter:
                            {
                                typeMapping = sqlParameter.TypeMapping;
                                var values = (IEnumerable)_parametersValues[sqlParameter.Name];
                                foreach (var value in values)
                                {
                                    if (value == null)
                                    {
                                        hasNullValue = true;
                                        continue;
                                    }

                                    inValues.Add(value);
                                }
                            }
                            break;
                    }

                    var updatedInExpression = inValues.Count > 0
                        ? _sqlExpressionFactory.In(
                            (SqlExpression)Visit(inExpression.Item),
                            _sqlExpressionFactory.Constant(inValues, typeMapping),
                            inExpression.Negated)
                        : null;

                    var nullCheckExpression = hasNullValue
                        ? _sqlExpressionFactory.IsNull(inExpression.Item)
                        : null;

                    if (updatedInExpression != null && nullCheckExpression != null)
                    {
                        return _sqlExpressionFactory.OrElse(updatedInExpression, nullCheckExpression);
                    }

                    if (updatedInExpression == null && nullCheckExpression == null)
                    {
                        return _sqlExpressionFactory.Equal(_sqlExpressionFactory.Constant(true), _sqlExpressionFactory.Constant(false));
                    }

                    return (SqlExpression)updatedInExpression ?? nullCheckExpression;
                }

                return base.Visit(expression);
            }
        }
    }
}
