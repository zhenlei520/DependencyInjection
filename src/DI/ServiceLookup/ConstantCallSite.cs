// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// 单例模式以具体实例注册时使用
    /// </summary>
    internal class ConstantCallSite : ServiceCallSite
    {
        /// <summary>
        /// 注册时提供的具体实例对象值
        /// </summary>
        internal object DefaultValue { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serviceType">注册的基类类型</param>
        /// <param name="defaultValue">其实际对象所对应的类型</param>
        public ConstantCallSite(Type serviceType, object defaultValue): base(ResultCache.None)
        {
            DefaultValue = defaultValue;
        }

        /// <summary>
        /// 注册的基类类型
        /// </summary>
        public override Type ServiceType => DefaultValue.GetType();
        
        /// <summary>
        /// 其实际对象所对应的类型
        /// </summary>
        public override Type ImplementationType => DefaultValue.GetType();
        
        /// <summary>
        /// 当前ServiceCallSite所对应的类型
        /// </summary>
        public override CallSiteKind Kind { get; } = CallSiteKind.Constant;
    }
}
