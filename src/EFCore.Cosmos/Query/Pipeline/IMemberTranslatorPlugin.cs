﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.Cosmos.Query.Pipeline
{
    /// <summary>
    ///     <para>
    ///         Represents plugin member translators.
    ///     </para>
    ///     <para>
    ///         The service lifetime is <see cref="ServiceLifetime.Singleton"/> and multiple registrations
    ///         are allowed. This means a single instance of each service is used by many <see cref="DbContext"/>
    ///         instances. The implementation must be thread-safe.
    ///         This service cannot depend on services registered as <see cref="ServiceLifetime.Scoped"/>.
    ///     </para>
    /// </summary>
    public interface IMemberTranslatorPlugin
    {
        /// <summary>
        ///     Gets the member translators.
        /// </summary>
        IEnumerable<IMemberTranslator> Translators { get; }
    }
}
