// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection.ServiceLookup;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// The default IServiceProvider.
    /// </summary>
    public sealed class ServiceProvider : IServiceProvider, IDisposable, IServiceProviderEngineCallback
    {
        /// <summary>
        /// ServiceProvider工作引擎接口
        /// 这个接口是一个核心接口
        /// 使用这个接口的子类进行调用缓存各种注册服务和调用访问者对象进行获取实例对象
        /// </summary>
        private readonly IServiceProviderEngine _engine;

        /// <summary>
        /// 此属性缓存当前注册类型,当ServiceProviderOptions.ValidateScopes为true进行验证
        /// </summary>
        private readonly CallSiteValidator _callSiteValidator;

        internal ServiceProvider(IEnumerable<ServiceDescriptor> serviceDescriptors, ServiceProviderOptions options)
        {
            IServiceProviderEngineCallback callback = null;
            if (options.ValidateScopes)
            {
                callback = this;
                _callSiteValidator = new CallSiteValidator();
            }
            switch (options.Mode)
            {
                case ServiceProviderMode.Dynamic:
                    _engine = new DynamicServiceProviderEngine(serviceDescriptors, callback);//实例化 DynamicServiceProviderEngine
                    break;
                case ServiceProviderMode.Runtime:
                    _engine = new RuntimeServiceProviderEngine(serviceDescriptors, callback);
                    break;
#if IL_EMIT
                case ServiceProviderMode.ILEmit:
                    _engine = new ILEmitServiceProviderEngine(serviceDescriptors, callback);
                    break;
#endif
                case ServiceProviderMode.Expressions:
                    _engine = new ExpressionsServiceProviderEngine(serviceDescriptors, callback);
                    break;
                default:
                    throw new NotSupportedException(nameof(options.Mode));
            }
        }

        /// <summary>
        /// Gets the service object of the specified type.
        /// </summary>
        /// <param name="serviceType"></param>
        /// <returns></returns>
        public object GetService(Type serviceType) => _engine.GetService(serviceType);

        /// <inheritdoc />
        public void Dispose() => _engine.Dispose();

        void IServiceProviderEngineCallback.OnCreate(ServiceCallSite callSite)
        {
            _callSiteValidator.ValidateCallSite(callSite);
        }

        void IServiceProviderEngineCallback.OnResolve(Type serviceType, IServiceScope scope)
        {
            _callSiteValidator.ValidateResolution(serviceType, scope, _engine.RootScope);
        }
    }
}
