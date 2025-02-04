﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Microsoft.EntityFrameworkCore.Relational.Query.Pipeline.SqlExpressions
{
    public class CaseExpression : SqlExpression
    {
        private readonly List<CaseWhenClause> _whenClauses = new List<CaseWhenClause>();

        public CaseExpression(
            SqlExpression operand,
            IReadOnlyList<CaseWhenClause> whenClauses)
            : this(operand, whenClauses, null)
        {
        }

        public CaseExpression(
            IReadOnlyList<CaseWhenClause> whenClauses,
            SqlExpression elseResult)
            : this(null, whenClauses, elseResult)
        {
        }

        private CaseExpression(
            SqlExpression operand,
            IReadOnlyList<CaseWhenClause> whenClauses,
            SqlExpression elseResult)
            : base(whenClauses[0].Result.Type, whenClauses[0].Result.TypeMapping)
        {
            Operand = operand;
            _whenClauses.AddRange(whenClauses);
            ElseResult = elseResult;
        }

        public SqlExpression Operand { get; }
        public IReadOnlyList<CaseWhenClause> WhenClauses => _whenClauses;
        public SqlExpression ElseResult { get; }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            var operand = (SqlExpression)visitor.Visit(Operand);
            var changed = operand != Operand;
            var whenClauses = new List<CaseWhenClause>();
            foreach (var whenClause in WhenClauses)
            {
                var test = (SqlExpression)visitor.Visit(whenClause.Test);
                var result = (SqlExpression)visitor.Visit(whenClause.Result);

                if (test != whenClause.Test || result != whenClause.Result)
                {
                    changed |= true;
                    whenClauses.Add(new CaseWhenClause(test, result));
                }
                else
                {
                    whenClauses.Add(whenClause);
                }
            }

            var elseResult = (SqlExpression)visitor.Visit(ElseResult);
            changed |= elseResult != ElseResult;

            return changed
                ? new CaseExpression(operand, whenClauses, elseResult)
                : this;
        }

        public virtual CaseExpression Update(
            SqlExpression operand,
            IReadOnlyList<CaseWhenClause> whenClauses,
            SqlExpression elseResult)
            => operand != Operand || !whenClauses.SequenceEqual(WhenClauses) || elseResult != ElseResult
                ? new CaseExpression(operand, whenClauses, elseResult)
                : this;

        public override void Print(ExpressionPrinter expressionPrinter)
        {
            expressionPrinter.StringBuilder.Append("CASE");
            if (Operand != null)
            {
                expressionPrinter.StringBuilder.Append(" ");
                expressionPrinter.Visit(Operand);
            }

            using (expressionPrinter.StringBuilder.Indent())
            {
                foreach (var whenClause in WhenClauses)
                {
                    expressionPrinter.StringBuilder.AppendLine().Append("WHEN ");
                    expressionPrinter.Visit(whenClause.Test);
                    expressionPrinter.StringBuilder.Append(" THEN ");
                    expressionPrinter.Visit(whenClause.Result);
                }

                if (ElseResult != null)
                {
                    expressionPrinter.StringBuilder.AppendLine().Append("ELSE ");
                    expressionPrinter.Visit(ElseResult);
                }
            }

            expressionPrinter.StringBuilder.AppendLine().Append("END");
        }

        public override bool Equals(object obj)
            => obj != null
            && (ReferenceEquals(this, obj)
                || obj is CaseExpression caseExpression
                    && Equals(caseExpression));

        private bool Equals(CaseExpression caseExpression)
            => base.Equals(caseExpression)
            && (Operand == null ? caseExpression.Operand == null : Operand.Equals(caseExpression.Operand))
            && WhenClauses.SequenceEqual(caseExpression.WhenClauses)
            && (ElseResult == null ? caseExpression.ElseResult == null : ElseResult.Equals(caseExpression.ElseResult));

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Operand);
            for (var i = 0; i < WhenClauses.Count; i++)
            {
                hash.Add(WhenClauses[i]);
            }
            hash.Add(ElseResult);
            return hash.ToHashCode();
        }
    }
}
