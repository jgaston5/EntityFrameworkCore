﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Relational.Query.Pipeline.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microsoft.EntityFrameworkCore.Relational.Query.Pipeline
{
    public interface ISqlExpressionFactory
    {
        SqlExpression ApplyTypeMapping(SqlExpression sqlExpression, RelationalTypeMapping typeMapping);
        SqlExpression ApplyDefaultTypeMapping(SqlExpression sqlExpression);
        RelationalTypeMapping GetTypeMappingForValue(object value);
        RelationalTypeMapping FindMapping(Type type);

        SqlBinaryExpression MakeBinary(ExpressionType operatorType, SqlExpression left, SqlExpression right, RelationalTypeMapping typeMapping);
        // Comparison
        SqlBinaryExpression Equal(SqlExpression left, SqlExpression right);
        SqlBinaryExpression NotEqual(SqlExpression left, SqlExpression right);
        SqlBinaryExpression GreaterThan(SqlExpression left, SqlExpression right);
        SqlBinaryExpression GreaterThanOrEqual(SqlExpression left, SqlExpression right);
        SqlBinaryExpression LessThan(SqlExpression left, SqlExpression right);
        SqlBinaryExpression LessThanOrEqual(SqlExpression left, SqlExpression right);
        // Logical
        SqlBinaryExpression AndAlso(SqlExpression left, SqlExpression right);
        SqlBinaryExpression OrElse(SqlExpression left, SqlExpression right);
        // Arithmetic
        SqlBinaryExpression Add(SqlExpression left, SqlExpression right, RelationalTypeMapping typeMapping = null);
        SqlBinaryExpression Subtract(SqlExpression left, SqlExpression right, RelationalTypeMapping typeMapping = null);
        SqlBinaryExpression Multiply(SqlExpression left, SqlExpression right, RelationalTypeMapping typeMapping = null);
        SqlBinaryExpression Divide(SqlExpression left, SqlExpression right, RelationalTypeMapping typeMapping = null);
        SqlBinaryExpression Modulo(SqlExpression left, SqlExpression right, RelationalTypeMapping typeMapping = null);
        // Bitwise
        SqlBinaryExpression And(SqlExpression left, SqlExpression right, RelationalTypeMapping typeMapping = null);
        SqlBinaryExpression Or(SqlExpression left, SqlExpression right, RelationalTypeMapping typeMapping = null);
        // Other
        SqlBinaryExpression Coalesce(SqlExpression left, SqlExpression right, RelationalTypeMapping typeMapping = null);

        SqlUnaryExpression IsNull(SqlExpression operand);
        SqlUnaryExpression IsNotNull(SqlExpression operand);
        SqlUnaryExpression Convert(SqlExpression operand, Type type, RelationalTypeMapping typeMapping = null);
        SqlUnaryExpression Not(SqlExpression operand);
        SqlUnaryExpression Negate(SqlExpression operand);

        CaseExpression Case(SqlExpression operand, params CaseWhenClause[] whenClauses);
        CaseExpression Case(IReadOnlyList<CaseWhenClause> whenClauses, SqlExpression elseResult);

        SqlFunctionExpression Function(
            string functionName, IEnumerable<SqlExpression> arguments, Type returnType, RelationalTypeMapping typeMapping = null);
        SqlFunctionExpression Function(
            string schema, string functionName, IEnumerable<SqlExpression> arguments, Type returnType, RelationalTypeMapping typeMapping = null);
        SqlFunctionExpression Function(
            SqlExpression instance, string functionName, IEnumerable<SqlExpression> arguments, Type returnType, RelationalTypeMapping typeMapping = null);
        SqlFunctionExpression Function(
            string functionName, Type returnType, RelationalTypeMapping typeMapping = null);
        SqlFunctionExpression Function(
            string schema, string functionName, Type returnType, RelationalTypeMapping typeMapping = null);
        SqlFunctionExpression Function(
            SqlExpression instance, string functionName, Type returnType, RelationalTypeMapping typeMapping = null);

        ExistsExpression Exists(SelectExpression subquery, bool negated);
        InExpression In(SqlExpression item, SqlExpression values, bool negated);
        InExpression In(SqlExpression item, SelectExpression subquery, bool negated);
        LikeExpression Like(SqlExpression match, SqlExpression pattern, SqlExpression escapeChar = null);
        SqlConstantExpression Constant(object value, RelationalTypeMapping typeMapping = null);
        SqlFragmentExpression Fragment(string sql);

        SelectExpression Select(SqlExpression projection);
        SelectExpression Select(IEntityType entityType);
        SelectExpression Select(IEntityType entityType, string sql, Expression sqlArguments);
    }
}
