﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Microsoft.EntityFrameworkCore.Cosmos.Query.Pipeline
{
    public class CosmosMethodCallTranslatorProvider : IMethodCallTranslatorProvider
    {
        private readonly List<IMethodCallTranslator> _plugins = new List<IMethodCallTranslator>();
        private readonly List<IMethodCallTranslator> _translators = new List<IMethodCallTranslator>();

        public CosmosMethodCallTranslatorProvider(
            ISqlExpressionFactory sqlExpressionFactory,
            IEnumerable<IMethodCallTranslatorPlugin> plugins)
        {
            _plugins.AddRange(plugins.SelectMany(p => p.Translators));

            _translators.AddRange(
                new IMethodCallTranslator[] {
                    new EqualsTranslator(sqlExpressionFactory),
                    //new StringMethodTranslator(sqlExpressionFactory),
                    new ContainsTranslator(sqlExpressionFactory),
                    //new LikeTranslator(sqlExpressionFactory),
                    //new EnumHasFlagTranslator(sqlExpressionFactory),
                    //new GetValueOrDefaultTranslator(sqlExpressionFactory),
                    //new ComparisonTranslator(sqlExpressionFactory),
                });
        }

        public SqlExpression Translate(IModel model, SqlExpression instance, MethodInfo method, IList<SqlExpression> arguments)
        {
            // TODO: UDF support. See issue#15338
            //var dbFunction = model.FindDbFunction(method);
            //if (dbFunction != null)
            //{
            //    return dbFunction.Translation?.Invoke(
            //            arguments.Select(e => _sqlExpressionFactory.ApplyDefaultTypeMapping(e)).ToList())
            //        ?? _sqlExpressionFactory.Function(
            //            dbFunction.Schema,
            //            dbFunction.FunctionName,
            //            arguments,
            //            method.ReturnType);
            //}

            return _plugins.Concat(_translators)
                .Select(t => t.Translate(instance, method, arguments))
                .FirstOrDefault(t => t != null);
        }

        protected virtual void AddTranslators(IEnumerable<IMethodCallTranslator> translators)
            => _translators.InsertRange(0, translators);
    }
}
