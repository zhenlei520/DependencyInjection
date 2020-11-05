// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// 以类型注册,也就是实例化对象时以构造函数实例化
    /// </summary>
    internal class ConstructorCallSite : ServiceCallSite
    {
        /// <summary>
        /// 实例化对象时所使用的构造器,当前构造器的最优构造器
        /// </summary>
        internal ConstructorInfo ConstructorInfo { get; }
        
        /// <summary>
        /// 当前构造器中所有参数的ServiceCallSite集合
        /// </summary>
        internal ServiceCallSite[] ParameterCallSites { get; }

        /// <summary>
        /// 最优构造器为无参
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="serviceType"></param>
        /// <param name="constructorInfo"></param>
        public ConstructorCallSite(ResultCache cache, Type serviceType, ConstructorInfo constructorInfo) : this(cache, serviceType, constructorInfo, Array.Empty<ServiceCallSite>())
        {
        }

        public ConstructorCallSite(ResultCache cache, Type serviceType, ConstructorInfo constructorInfo, ServiceCallSite[] parameterCallSites) : base(cache)
        {
            ServiceType = serviceType;
            ConstructorInfo = constructorInfo;
            ParameterCallSites = parameterCallSites;
        }

        public override Type ServiceType { get; }

        /// <summary>
        /// 使用构造器的DeclaringType
        /// </summary>
        public override Type ImplementationType => ConstructorInfo.DeclaringType;
        public override CallSiteKind Kind { get; } = CallSiteKind.Constructor;
    }
}
