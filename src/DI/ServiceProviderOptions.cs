// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Options for configuring various behaviors of the default <see cref="IServiceProvider"/> implementation.
    /// </summary>
    public class ServiceProviderOptions
    {
        // Avoid allocating objects in the default case
        internal static readonly ServiceProviderOptions Default = new ServiceProviderOptions();

        /// <summary>
        /// <c>true</c> to perform check verifying that scoped services never gets resolved from root provider; otherwise <c>false</c>.
        /// 是否需要检查生命周期
        /// 此属性为true,不能从获取顶级容器中的scoped
        /// </summary>
        public bool ValidateScopes { get; set; }

        /// <summary>
        /// 实例化ServiceProvider模式,当前只能使用Dynamic模式
        /// </summary>
        internal ServiceProviderMode Mode { get; set; } = ServiceProviderMode.Dynamic;
    }
}
