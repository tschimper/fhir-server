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
        /// If set, it will be used to adress the S3 Storage with this URL (Neccesary e.g. for S3 on Premise.e.g. Cloudian)
        /// </summary>
        public System.Uri S3StorageURL { get; set; }

        /// <summary>
        /// This is the S3 Authentication String (i.e. User ID)
        /// </summary>
        public string AuthentificationString { get; set; }

        /// <summary>
        /// This is the S3 Secret String (I.e. Password)
        /// </summary>
        public string SecretString { get; set; }

        /// <summary>
        /// This Swiches between "AWS" (with Region EUCentral) and (S3 on Premise e.g.Cloudian (S3StorageURL will be used instead)
        /// </summary>
        public string S3Type { get; set; }

        /// <summary>
        /// This sets the Instance as prefix for Bucket Names, to run multiple instances on one S3-Storage
        /// </summary>
        public string S3Instance { get; set; }
    }
}
