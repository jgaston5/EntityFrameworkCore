// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Microsoft.EntityFrameworkCore.Cosmos.Metadata.Conventions.Internal
{
    public class CosmosConventionSetBuilder : ProviderConventionSetBuilder
    {
        public CosmosConventionSetBuilder(
            [NotNull] ProviderConventionSetBuilderDependencies dependencies)
            : base(dependencies)
        {
        }

        public override ConventionSet CreateConventionSet()
        {
            var conventionSet = base.CreateConventionSet();

            conventionSet.ModelInitializedConventions.Add(new ContextContainerNameConvention(Dependencies));

            var discriminatorConvention = new CosmosDiscriminatorConvention(Dependencies);
            var storeKeyConvention = new StoreKeyConvention(Dependencies);
            conventionSet.EntityTypeAddedConventions.Add(storeKeyConvention);
            conventionSet.EntityTypeAddedConventions.Add(discriminatorConvention);

            ReplaceConvention(conventionSet.EntityTypeRemovedConventions, (DiscriminatorConvention)discriminatorConvention);

            conventionSet.EntityTypeBaseTypeChangedConventions.Add(storeKeyConvention);
            ReplaceConvention(conventionSet.EntityTypeBaseTypeChangedConventions, (DiscriminatorConvention)discriminatorConvention);

            conventionSet.ForeignKeyOwnershipChangedConventions.Add(storeKeyConvention);

            conventionSet.EntityTypeAnnotationChangedConventions.Add(storeKeyConvention);

            return conventionSet;
        }
    }
}
