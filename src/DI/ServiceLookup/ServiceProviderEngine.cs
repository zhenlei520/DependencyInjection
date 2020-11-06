// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// 类型是整个结构的核心类型,但是这个类也是一个很简单的类，
    /// 只是调用`CallSiteFactory`和`CallSiteRuntimeResolver`,由下图可以看到这个类型是一个抽象类,并且实现了`IServiceProviderEngine`和`IServiceScopeFactory`接口接口
    /// </summary>
    internal abstract class ServiceProviderEngine : IServiceProviderEngine, IServiceScopeFactory
    {
        /// <summary>
        /// 顶级容器时检查scoped生命周期的访问者对象，这个从ServiceProvider类中时进行传入的
        /// 顶级容器时scoped生命周期实例检查策略 
        /// </summary>
        private readonly IServiceProviderEngineCallback _callback;

        /// <summary>
        /// 一个根据类型获取一个根据容器获取服务实例对象的委托,可以看到使用了一个CreateServiceAccessor()进行赋值,CreateServiceAccessor()是此类型的一个核心方法
        /// 根据类型创建构建服务的委托
        /// </summary>
        private readonly Func<Type, Func<ServiceProviderEngineScope, object>> _createServiceAccessor;

        /// <summary>
        /// 此实例是否被销毁
        /// </summary>
        private bool _disposed;

        protected ServiceProviderEngine(IEnumerable<ServiceDescriptor> serviceDescriptors, IServiceProviderEngineCallback callback)
        {
            _createServiceAccessor = CreateServiceAccessor;
            _callback = callback;
            Root = new ServiceProviderEngineScope(this);//实例化根容器
            RuntimeResolver = new CallSiteRuntimeResolver();//实例化 CallSite对象访问者对象
            CallSiteFactory = new CallSiteFactory(serviceDescriptors);
            CallSiteFactory.Add(typeof(IServiceProvider), new ServiceProviderCallSite());
            CallSiteFactory.Add(typeof(IServiceScopeFactory), new ServiceScopeFactoryCallSite());// 缓存一个ServiceScopeFactoryCallSite服务,相当于缓存一个ServiceProviderEngine,根据此对象进行创建子容器
            RealizedServices = new ConcurrentDictionary<Type, Func<ServiceProviderEngineScope, object>>();//缓存实例化对象的工厂
        }

        /// <summary>
        /// 缓存根据容器获取服务实例对象委托,其中Key为ServiceType
        /// 缓存根据容器获取服务实例的委托，  Key为注册类型
        /// </summary>
        internal ConcurrentDictionary<Type, Func<ServiceProviderEngineScope, object>> RealizedServices { get; }

        /// <summary>
        /// 工厂类型，在构造器中实例化，可以看到实例化时将serviceDescriptors进行传入，并且可以看到在构造器中向此实例对象中添加了一个IServiceProvider和IServiceScopeFactory
        /// CallSite工厂类属性,此类型用于根据指定实例化方式来创建对应的CallSite
        /// </summary>
        internal CallSiteFactory CallSiteFactory { get; }

        /// <summary>
        /// 是获取服务实例的访问者对象,可以看到在构造器中进行传入
        /// 访问者对象,此对象对进行实例和缓存具体真正的对象
        /// </summary>
        protected CallSiteRuntimeResolver RuntimeResolver { get; }

        /// <summary>
        /// 一个顶级容器ServiceProviderEngineScope类型则是一个具体的容器类型,这个类型中缓存了所有的具体服务实例对象,这个类型实现了IServiceScope接口,从下面代码可以看到RootScope其实就是直接返回了Root属性
        /// </summary>
        public ServiceProviderEngineScope Root { get; }

        /// <summary>
        /// 这也是一个根容器实例对象，直接返回的Root属性
        /// </summary>
        public IServiceScope RootScope => Root;

        public object GetService(Type serviceType) => GetService(serviceType, Root);

        /// <summary>
        /// 由派生类继承，由指定的ServiceCallSite缓存并获取 服务实例的委托
        /// 抽象类型,子类实现
        /// </summary>
        /// <param name="callSite"></param>
        /// <returns></returns>
        protected abstract Func<ServiceProviderEngineScope, object> RealizeService(ServiceCallSite callSite);

        /// <summary>
        /// 清除当前对象，并清除顶级容器
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            Root.Dispose();
        }

        /// <summary>
        /// 这个方法获取服务实例对象，可以看到具有两个此方法，并且第一个调用了第二个，并将顶级容器Root进行了传入，而在第二个方法中，获取并添加_createServiceAccessor委托，然后调用此委托进行获取服务实例
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="serviceProviderEngineScope"></param>
        /// <returns></returns>
        internal object GetService(Type serviceType, ServiceProviderEngineScope serviceProviderEngineScope)
        {
            if (_disposed)
            {
                ThrowHelper.ThrowObjectDisposedException();
            }

            var realizedService = RealizedServices.GetOrAdd(serviceType, _createServiceAccessor);//添加并获取根据容器对象实例化对象的方法,其方法由子类进行重写
            _callback?.OnResolve(serviceType, serviceProviderEngineScope);// 验证是否允许进行实例化对象
            return realizedService.Invoke(serviceProviderEngineScope);
        }

        /// <summary>
        /// 这个方法是创建一个子容器对象，在这个方法中可以看到直接 new 了一个容器对象，并将当前对象进行了传入。从此可以得知为什么所有容器共享顶级容器的服务注册了
        /// 实例化的子容器
        /// </summary>
        /// <returns></returns>
        public IServiceScope CreateScope()
        {
            if (_disposed)
            {
                ThrowHelper.ThrowObjectDisposedException();
            }

            return new ServiceProviderEngineScope(this);
        }

        /// <summary>
        /// 看到根据ServiceType进行获取指定ServiceCallSite,然后再调用派生类实现的RealizeService()进行返回
        /// </summary>
        /// <param name="serviceType"></param>
        /// <returns></returns>
        private Func<ServiceProviderEngineScope, object> CreateServiceAccessor(Type serviceType)
        {
            var callSite = CallSiteFactory.GetCallSite(serviceType, new CallSiteChain());
            if (callSite != null)
            {
                _callback?.OnCreate(callSite);
                return RealizeService(callSite);
            }

            return _ => null;
        }
    }
}