// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.S3Storage.Features.Storage;

namespace Microsoft.Health.Fhir.S3Storage.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal readonly struct SearchParameterQueryGeneratorContext
    {
        internal SearchParameterQueryGeneratorContext(IndentedStringBuilder stringBuilder, S3SqlQueryParameterManager parameters, S3StorageFhirModel model, string tableAlias = null)
        {
            EnsureArg.IsNotNull(stringBuilder, nameof(stringBuilder));
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            EnsureArg.IsNotNull(model, nameof(model));

            StringBuilder = stringBuilder;
            Parameters = parameters;
            Model = model;
            TableAlias = tableAlias;
        }

        public IndentedStringBuilder StringBuilder { get; }

        public S3SqlQueryParameterManager Parameters { get; }

        public S3StorageFhirModel Model { get; }

        public string TableAlias { get; }
    }
}
