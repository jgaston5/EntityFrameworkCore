﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Relational.Query.Expressions.Internal
{
    public abstract class TableExpressionBase : Expression, IPrintable
    {
        protected TableExpressionBase([CanBeNull] string alias)
        {
            Check.NullButNotEmpty(alias, nameof(alias));

            Alias = alias;
        }

        public string Alias { get; internal set; }

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

        public override Type Type => typeof(object);
        public override ExpressionType NodeType => ExpressionType.Extension;
        public abstract void Print(ExpressionPrinter expressionPrinter);
        public override bool Equals(object obj)
            => obj != null
            && (ReferenceEquals(this, obj)
                || obj is TableExpressionBase tableExpressionBase
                    && Equals(tableExpressionBase));

        private bool Equals(TableExpressionBase tableExpressionBase)
            => string.Equals(Alias, tableExpressionBase.Alias);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Alias?.GetHashCode() ?? 0);

                return hashCode;
            }
        }
    }
}
