// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.S3Storage.Features.Schema
{
    public class S3SchemaInformation
    {
        public S3SchemaInformation()
        {
            MinimumSupportedVersion = S3SchemaVersion.V1;
            MaximumSupportedVersion = S3SchemaVersion.V1;
        }

        public S3SchemaVersion MinimumSupportedVersion { get; }

        public S3SchemaVersion MaximumSupportedVersion { get; }

        public S3SchemaVersion? Current { get; set; }
    }
}
