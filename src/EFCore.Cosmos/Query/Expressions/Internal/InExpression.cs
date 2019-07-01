﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microsoft.EntityFrameworkCore.Cosmos.Query.Expressions.Internal
{
    public class InExpression : SqlExpression
    {
        public InExpression(SqlExpression item, bool negated, SqlExpression values, CoreTypeMapping typeMapping)
           : base(typeof(bool), typeMapping)
        {
            Item = item;
            Negated = negated;
            Values = values;
        }

        public SqlExpression Item { get; }
        public bool Negated { get; }
        public SqlExpression Values { get; }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            var newItem = (SqlExpression)visitor.Visit(Item);
            var values = (SqlExpression)visitor.Visit(Values);

            return Update(newItem, values);
        }

        public InExpression Negate() => new InExpression(Item, !Negated, Values, TypeMapping);

        public InExpression Update(SqlExpression item, SqlExpression values)
            => item != Item || values != Values
                ? new InExpression(item, Negated, values, TypeMapping)
                : this;

        public override void Print(ExpressionPrinter expressionPrinter)
        {
            expressionPrinter.Visit(Item);
            expressionPrinter.StringBuilder.Append(Negated ? " NOT IN " : " IN ");
            expressionPrinter.StringBuilder.Append("(");
            expressionPrinter.Visit(Values);
            expressionPrinter.StringBuilder.Append(")");
        }

        public override bool Equals(object obj)
            => obj != null
            && (ReferenceEquals(this, obj)
                || obj is InExpression inExpression
                    && Equals(inExpression));

        private bool Equals(InExpression inExpression)
            => base.Equals(inExpression)
            && Item.Equals(inExpression.Item)
            && Negated.Equals(inExpression.Negated)
            && (Values == null ? inExpression.Values == null : Values.Equals(inExpression.Values));

        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Item, Negated, Values);
    }
}
