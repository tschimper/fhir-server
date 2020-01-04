// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.S3Storage.Features.Schema.Model;

namespace Microsoft.Health.Fhir.S3Storage.Features.Storage.TvpRowGeneration
{
    internal class ResourceWriteClaimRowGenerator : ITableValuedParameterRowGenerator<S3ResourceMetadata, V1.ResourceWriteClaimTableTypeRow>
    {
        private readonly S3StorageFhirModel _model;

        public ResourceWriteClaimRowGenerator(S3StorageFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            _model = model;
        }

        public IEnumerable<V1.ResourceWriteClaimTableTypeRow> GenerateRows(S3ResourceMetadata resourceMetadata)
        {
            return resourceMetadata.WriteClaims?.Select(c =>
                new V1.ResourceWriteClaimTableTypeRow(_model.GetClaimTypeId(c.Key), c.Value));
        }
    }
}
