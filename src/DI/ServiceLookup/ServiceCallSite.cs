// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// Summary description for IServiceCallSite
    /// </summary>
    internal abstract class ServiceCallSite
    {
        protected ServiceCallSite(ResultCache cache)
        {
            Cache = cache;
        }

        /// <summary>
        /// 当前注册的服务类型
        /// </summary>
        public abstract Type ServiceType { get; }
        
        /// <summary>
        /// 当前注册的实例化类型
        /// </summary>
        public abstract Type ImplementationType { get; }
        
        /// <summary>
        ///  当前CallSite所属的类型
        /// </summary>
        public abstract CallSiteKind Kind { get; }
        
        /// <summary>
        /// 服务实例对象的缓存配置
        /// </summary>
        public ResultCache Cache { get; }
    }
}