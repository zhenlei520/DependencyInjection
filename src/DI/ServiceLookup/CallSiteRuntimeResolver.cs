using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// 一个创建或获取服务实例的类型
    /// 创建和获取服务实例类型的访问者，这个类型泛型参数分别为RuntimeResolverContext类型和实例对象类型Object
    /// </summary>
    internal sealed class CallSiteRuntimeResolver : CallSiteVisitor<RuntimeResolverContext, object>
    {
        public object Resolve(ServiceCallSite callSite, ServiceProviderEngineScope scope)
        {
            return VisitCallSite(callSite, new RuntimeResolverContext
            {
                Scope = scope
            });
        }

        protected override object VisitDisposeCache(ServiceCallSite transientCallSite, RuntimeResolverContext context)
        {
            return context.Scope.CaptureDisposable(VisitCallSiteMain(transientCallSite, context));
        }

        protected override object VisitConstructor(ConstructorCallSite constructorCallSite, RuntimeResolverContext context)
        {
            object[] parameterValues;
            if (constructorCallSite.ParameterCallSites.Length == 0)
            {
                parameterValues = Array.Empty<object>();
            }
            else
            {
                //如果当前构造器参数不为空,则实例化每一个参数的实例对象
                parameterValues = new object[constructorCallSite.ParameterCallSites.Length];
                for (var index = 0; index < parameterValues.Length; index++)
                {
                    parameterValues[index] = VisitCallSite(constructorCallSite.ParameterCallSites[index], context);
                }
            }

            try
            {
                //根据参数对象进行实例化对象并返回
                return constructorCallSite.ConstructorInfo.Invoke(parameterValues);
            }
            catch (Exception ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                // The above line will always throw, but the compiler requires we throw explicitly.
                throw;
            }
        }

        protected override object VisitRootCache(ServiceCallSite singletonCallSite, RuntimeResolverContext context)
        {
            return VisitCache(singletonCallSite, context, context.Scope.Engine.Root, RuntimeResolverLock.Root);
        }

        protected override object VisitScopeCache(ServiceCallSite singletonCallSite, RuntimeResolverContext context)
        {
            // Check if we are in the situation where scoped service was promoted to singleton
            // and we need to lock the root
            var requiredScope = context.Scope == context.Scope.Engine.Root ?
                RuntimeResolverLock.Root :
                RuntimeResolverLock.Scope;

            return VisitCache(singletonCallSite, context, context.Scope, requiredScope);
        }

        private object VisitCache(ServiceCallSite scopedCallSite, RuntimeResolverContext context, ServiceProviderEngineScope serviceProviderEngine, RuntimeResolverLock lockType)
        {
            bool lockTaken = false;
            var resolvedServices = serviceProviderEngine.ResolvedServices;

            // Taking locks only once allows us to fork resolution process
            // on another thread without causing the deadlock because we
            // always know that we are going to wait the other thread to finish before
            // releasing the lock
            if ((context.AcquiredLocks & lockType) == 0)
            {
                Monitor.Enter(resolvedServices, ref lockTaken);
            }

            try
            {
                if (!resolvedServices.TryGetValue(scopedCallSite.Cache.Key, out var resolved))
                {
                    resolved = VisitCallSiteMain(scopedCallSite, new RuntimeResolverContext
                    {
                        Scope = serviceProviderEngine,
                        AcquiredLocks = context.AcquiredLocks | lockType
                    });

                    serviceProviderEngine.CaptureDisposable(resolved);
                    resolvedServices.Add(scopedCallSite.Cache.Key, resolved);
                }

                return resolved;
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(resolvedServices);
                }
            }
        }

        protected override object VisitConstant(ConstantCallSite constantCallSite, RuntimeResolverContext context)
        {
            return constantCallSite.DefaultValue;//直接返回ConstantCallSite的值
        }

        protected override object VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, RuntimeResolverContext context)
        {
            return context.Scope;//直接返回RuntimeResolverContext封装的容器
        }

        protected override object VisitServiceScopeFactory(ServiceScopeFactoryCallSite serviceScopeFactoryCallSite, RuntimeResolverContext context)
        {
            return context.Scope.Engine;//直接返回容器内的ServiceProviderEngine
        }

        protected override object VisitIEnumerable(IEnumerableCallSite enumerableCallSite, RuntimeResolverContext context)
        {
            var array = Array.CreateInstance(
                enumerableCallSite.ItemType,
                enumerableCallSite.ServiceCallSites.Length);

            for (var index = 0; index < enumerableCallSite.ServiceCallSites.Length; index++)
            {
                var value = VisitCallSite(enumerableCallSite.ServiceCallSites[index], context);//实例化IEnumerableCallSite.ServiceCallSites中所有的服务实例对象并赋值到数组中
                array.SetValue(value, index);
            }
            return array;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="factoryCallSite"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        protected override object VisitFactory(FactoryCallSite factoryCallSite, RuntimeResolverContext context)
        {
            //调用工厂方法进行实例化
            return factoryCallSite.Factory(context.Scope);
        }
    }

    /// <summary>
    /// ServiceProviderEngineScope封装类型
    /// </summary>
    internal struct RuntimeResolverContext
    {
        public ServiceProviderEngineScope Scope { get; set; }

        public RuntimeResolverLock AcquiredLocks { get; set; }
    }

    [Flags]
    internal enum RuntimeResolverLock
    {
        Scope = 1,
        Root = 2
    }
}