// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// 获取子容器所使用,在Engine类中会注册此类实例,然后获取子类容器使用
    /// </summary>
    internal class ServiceScopeFactoryCallSite : ServiceCallSite
    {
        public ServiceScopeFactoryCallSite() : base(ResultCache.None)
        {
        }

        public override Type ServiceType { get; } = typeof(IServiceScopeFactory);
        
        /// <summary>
        /// IServiceProviderEngine派生类型,这个类型也实现了IServiceScopeFactory接口,所以是一个子容器工厂类型
        /// </summary>
        public override Type ImplementationType { get; } = typeof(ServiceProviderEngine);
        public override CallSiteKind Kind { get; } = CallSiteKind.ServiceScopeFactory;
    }
}
