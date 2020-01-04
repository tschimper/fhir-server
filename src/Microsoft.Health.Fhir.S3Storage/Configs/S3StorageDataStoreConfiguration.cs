// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.S3Storage.Configs
{
    public class S3StorageDataStoreConfiguration
    {
        public string ConnectionString { get; set; }

        /// <summary>
        /// Allows the experimental schema initializer to attempt to bring the schema to the minimum supported version.
        /// </summary>
        public bool Initialize { get; set; }

        /// <summary>
        /// WARNING: THIS RESETS ALL DATA IN THE DATABASE
        /// If set, this applies schema 1 which resets all the data in the database. This is temporary until the schema migration tool is complete.
        /// </summary>
        public bool DeleteAllDataOnStartup { get; set; }

        /// <summary>
        /// WARNING: THIS RESETS ALL DATA IN THE DATABASE
        /// If set, this applies schema 1 which resets all the data in the database. This is temporary until the schema migration tool is complete.
        /// </summary>
        public System.Uri S3StorageURL { get; set; }

        /// <summary>
        /// WARNING: THIS RESETS ALL DATA IN THE DATABASE
        /// If set, this applies schema 1 which resets all the data in the database. This is temporary until the schema migration tool is complete.
        /// </summary>
        public string AuthentificationString { get; set; }

        /// <summary>
        /// WARNING: THIS RESETS ALL DATA IN THE DATABASE
        /// If set, this applies schema 1 which resets all the data in the database. This is temporary until the schema migration tool is complete.
        /// </summary>
        public string SecretString { get; set; }
    }
}
