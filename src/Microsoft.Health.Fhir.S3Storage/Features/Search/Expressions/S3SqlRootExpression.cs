// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions
{
    /// <summary>
    /// The root of a search expression tree that will be translated to a SQL command.
    /// It is organized as a set of "normalized" table expression predicates and a set
    /// of "denormalized" predicates. The normalized predicates are over a search parameter
    /// table, whereas denormalized predicates are applied to the Resource table directly. Some
    /// of them can be applied to search parameter tables as well.
    /// </summary>
    internal class S3SqlRootExpression : Expression
    {
        public S3SqlRootExpression(IReadOnlyList<S3TableExpression> tableExpressions, IReadOnlyList<Expression> denormalizedExpressions)
        {
            EnsureArg.IsNotNull(tableExpressions, nameof(tableExpressions));
            EnsureArg.IsNotNull(denormalizedExpressions, nameof(denormalizedExpressions));

            TableExpressions = tableExpressions;
            DenormalizedExpressions = denormalizedExpressions;
        }

        public IReadOnlyList<S3TableExpression> TableExpressions { get; }

        public IReadOnlyList<Expression> DenormalizedExpressions { get; }

        public static S3SqlRootExpression WithTableExpressions(params S3TableExpression[] expressions)
        {
            return new S3SqlRootExpression(expressions, Array.Empty<Expression>());
        }

        public static S3SqlRootExpression WithDenormalizedExpressions(params Expression[] expressions)
        {
            return new S3SqlRootExpression(Array.Empty<S3TableExpression>(), expressions);
        }

        public static S3SqlRootExpression WithTableExpressions(IReadOnlyList<S3TableExpression> expressions)
        {
            return new S3SqlRootExpression(expressions, Array.Empty<Expression>());
        }

        public static S3SqlRootExpression WithDenormalizedExpressions(IReadOnlyList<Expression> expressions)
        {
            return new S3SqlRootExpression(Array.Empty<S3TableExpression>(), expressions);
        }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return AcceptVisitor((IS3SqlExpressionVisitor<TContext, TOutput>)visitor, context);
        }

        public TOutput AcceptVisitor<TContext, TOutput>(IS3SqlExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitSqlRoot(this, context);
        }

        public override string ToString()
        {
            return $"(SqlRoot (Joined{(TableExpressions.Any() ? " " + string.Join(" ", TableExpressions) : null)}) (Resource{(DenormalizedExpressions.Any() ? " " + string.Join(" ", DenormalizedExpressions) : null)}))";
        }
    }
}
