﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using GeoAPI.Geometries;
using Microsoft.EntityFrameworkCore.Relational.Query.Pipeline;
using Microsoft.EntityFrameworkCore.Relational.Query.Expressions.Internal;

namespace Microsoft.EntityFrameworkCore.SqlServer.Query.Pipeline
{
    public class SqlServerMultiCurveMemberTranslator : IMemberTranslator
    {
        private static readonly MemberInfo _isClosed = typeof(IMultiCurve).GetRuntimeProperty(nameof(IMultiCurve.IsClosed));
        private readonly ISqlExpressionFactory _sqlExpressionFactory;

        public SqlServerMultiCurveMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
        {
            _sqlExpressionFactory = sqlExpressionFactory;
        }

        public SqlExpression Translate(SqlExpression instance, MemberInfo member, Type returnType)
        {
            if (Equals(member.OnInterface(typeof(IMultiCurve)), _isClosed))
            {
                return _sqlExpressionFactory.Function(
                    instance,
                    "STIsClosed",
                    Array.Empty<SqlExpression>(),
                    returnType);
            }

            return null;
        }
    }
}
