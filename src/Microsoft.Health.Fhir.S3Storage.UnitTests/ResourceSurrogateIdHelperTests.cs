// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.S3Storage.Features.Storage;
using Xunit;

namespace Microsoft.Health.Fhir.S3Storage.UnitTests
{
    public class ResourceSurrogateIdHelperTests
    {
        [Fact]
        public void GivenADateTime_WhenRepresentedAsASurrogateId_HasTheExpectedRange()
        {
            var baseDate = DateTime.MinValue;
            long baseId = S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(baseDate);

            Assert.Equal(baseDate, S3ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(baseId + 79999));
            Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond), S3ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(baseId + 80000) - baseDate);

            long maxBaseId = S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(S3ResourceSurrogateIdHelper.MaxDateTime);

            Assert.Equal(S3ResourceSurrogateIdHelper.MaxDateTime.TruncateToMillisecond(), S3ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(maxBaseId));
            Assert.Equal(S3ResourceSurrogateIdHelper.MaxDateTime.TruncateToMillisecond(), S3ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(maxBaseId + 79999));
        }

        [Fact]
        public void GivenADateTimeLargerThanTheLargestThatCanBeRepresentedAsASurrogateId_WhenTurnedIntoASurrogateId_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => S3ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.MaxValue));
        }
    }
}
