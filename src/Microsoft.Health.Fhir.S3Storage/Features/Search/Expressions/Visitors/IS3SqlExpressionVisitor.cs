// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors
{
    internal interface IS3SqlExpressionVisitor<in TContext, out TOutput> : IExpressionVisitor<TContext, TOutput>
    {
        TOutput VisitSqlRoot(S3SqlRootExpression expression, TContext context);

        TOutput VisitTable(S3TableExpression tableExpression, TContext context);
    }
}
