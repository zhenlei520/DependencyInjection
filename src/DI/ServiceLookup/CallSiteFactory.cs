// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// 构造方法工厂
    /// </summary>
    internal class CallSiteFactory
    {
        private const int DefaultSlot = 0;
        private readonly List<ServiceDescriptor> _descriptors;

        private readonly ConcurrentDictionary<Type, ServiceCallSite> _callSiteCache =
            new ConcurrentDictionary<Type, ServiceCallSite>();

        private readonly Dictionary<Type, ServiceDescriptorCacheItem> _descriptorLookup =
            new Dictionary<Type, ServiceDescriptorCacheItem>();

        private readonly StackGuard _stackGuard;

        public CallSiteFactory(IEnumerable<ServiceDescriptor> descriptors)
        {
            _stackGuard = new StackGuard();
            _descriptors = descriptors.ToList();
            Populate(descriptors);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="descriptors"></param>
        /// <exception cref="ArgumentException"></exception>
        private void Populate(IEnumerable<ServiceDescriptor> descriptors)
        {
            foreach (var descriptor in descriptors)
            {
                var serviceTypeInfo = descriptor.ServiceType.GetTypeInfo();
                if (serviceTypeInfo.IsGenericTypeDefinition)
                {
                    //判断基类是不是泛型类
                    var implementationTypeInfo = descriptor.ImplementationType?.GetTypeInfo();

                    if (implementationTypeInfo == null || !implementationTypeInfo.IsGenericTypeDefinition)
                    {
                        //那么如果其实际类型implementationTypeInfo类不是泛型类或者为抽象类,那么就抛出异常
                        throw new ArgumentException(
                            Resources.FormatOpenGenericServiceRequiresOpenGenericImplementation(descriptor.ServiceType),
                            nameof(descriptors));
                    }

                    if (implementationTypeInfo.IsAbstract || implementationTypeInfo.IsInterface)
                    {
                        //那么如果其实际类型implementationTypeInfo类是抽象类或者是接口，就抛出异常
                        throw new ArgumentException(
                            Resources.FormatTypeCannotBeActivated(descriptor.ImplementationType,
                                descriptor.ServiceType));
                    }
                }
                else if (descriptor.ImplementationInstance == null && descriptor.ImplementationFactory == null)
                {
                    //如果当前基类不为泛型类
                    Debug.Assert(descriptor.ImplementationType != null);
                    var implementationTypeInfo = descriptor.ImplementationType.GetTypeInfo();

                    if (implementationTypeInfo.IsGenericTypeDefinition ||
                        implementationTypeInfo.IsAbstract ||
                        implementationTypeInfo.IsInterface)
                    {
                        //那么如果其实际类型为泛型类或者是抽象类型,那么就抛出异常
                        throw new ArgumentException(
                            Resources.FormatTypeCannotBeActivated(descriptor.ImplementationType,
                                descriptor.ServiceType));
                    }
                }

                var cacheKey = descriptor.ServiceType;
                _descriptorLookup.TryGetValue(cacheKey, out var cacheItem);
                _descriptorLookup[cacheKey] = cacheItem.Add(descriptor);
            }
        }

        internal ServiceCallSite GetCallSite(Type serviceType, CallSiteChain callSiteChain)
        {
#if NETCOREAPP2_0
            return _callSiteCache.GetOrAdd(serviceType, (type, chain) => CreateCallSite(type, chain), callSiteChain);
#else
            return _callSiteCache.GetOrAdd(serviceType, type => CreateCallSite(type, callSiteChain));
#endif
        }

        private ServiceCallSite CreateCallSite(Type serviceType, CallSiteChain callSiteChain)
        {
            if (!_stackGuard.TryEnterOnCurrentStack())
            {
                return _stackGuard.RunOnEmptyStack((type, chain) => CreateCallSite(type, chain), serviceType,
                    callSiteChain);
            }

            ServiceCallSite callSite;
            try
            {
                //检查是否已被创建,如果已创建,则抛出异常
                callSiteChain.CheckCircularDependency(serviceType);

                //获取指定服务的实例对象方式
                // 1.首先创建普通类型的ServiceCallSite
                // 2.创建泛型类型的ServiceCallSite
                // 3.如果服务类型是集合.那么将获取当前类型所有实现对象
                callSite = TryCreateExact(serviceType, callSiteChain) ??
                           TryCreateOpenGeneric(serviceType, callSiteChain) ??
                           TryCreateEnumerable(serviceType, callSiteChain);
            }
            finally
            {
                callSiteChain.Remove(serviceType);
            }

            _callSiteCache[serviceType] = callSite;

            return callSite;
        }

        #region 创建普通类型的ServiceCallSite

        /// <summary>
        /// 创建普通类型的ServiceCallSite
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="callSiteChain"></param>
        /// <returns></returns>
        private ServiceCallSite TryCreateExact(Type serviceType, CallSiteChain callSiteChain)
        {
            // 在_descriptorLookup缓存中获取指定基类的所有ServiceDescriptor实例,
            //  然后利用最后一个ServiceDescriptor进行实例化ServiceCallSite
            if (_descriptorLookup.TryGetValue(serviceType, out var descriptor))
            {
                return TryCreateExact(descriptor.Last, serviceType, callSiteChain, DefaultSlot);
            }

            return null;
        }

        #endregion

        #region 创建泛型类型的ServiceCallSite

        /// <summary>
        /// 创建泛型类型的ServiceCallSite
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="callSiteChain"></param>
        /// <returns></returns>
        private ServiceCallSite TryCreateOpenGeneric(Type serviceType, CallSiteChain callSiteChain)
        {
            //会判断此泛型是否是封闭类型,此类型是否存在于_descriptorLookup,然后调用TryCreateOpenGeneric()进行获取ServiceCallSite
            if (serviceType.IsConstructedGenericType
                && _descriptorLookup.TryGetValue(serviceType.GetGenericTypeDefinition(), out var descriptor))
            {
                return TryCreateOpenGeneric(descriptor.Last, serviceType, callSiteChain, DefaultSlot);
            }

            return null;
        }

        #endregion

        #region 服务类型是集合.那么将获取当前类型所有实现对象

        /// <summary>
        /// 服务类型是集合.那么将获取当前类型所有实现对象
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="callSiteChain"></param>
        /// <returns></returns>
        private ServiceCallSite TryCreateEnumerable(Type serviceType, CallSiteChain callSiteChain)
        {
            //类型是封闭泛型类型并且泛型集合为IEnumerable
            if (serviceType.IsConstructedGenericType &&
                serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                //获取当前注册类型集合的泛型参数,由此类型来当基类类型进行获取注册当前类型的所有服务ServiceCallSite
                var itemType = serviceType.GenericTypeArguments.Single();
                callSiteChain.Add(serviceType);

                var callSites = new List<ServiceCallSite>();

                // If item type is not generic we can safely use descriptor cache
                if (!itemType.IsConstructedGenericType &&
                    _descriptorLookup.TryGetValue(itemType, out var descriptors))
                {
                    //如果泛型类型不是泛型并存在于缓存中
                    for (int i = 0; i < descriptors.Count; i++)
                    {
                        //一次获取其中每一个ServiceDecriptor然后创建对应的ServiceCallSite
                        var descriptor = descriptors[i];

                        //  设置当前slot
                        //   slot为倒序设置
                        // Last service should get slot 0
                        var slot = descriptors.Count - i - 1;
                        // There may not be any open generics here
                        //      获取当前ServiceDecriptor的ServiceCallSite并添加数组中

                        // There may not be any open generics here
                        var callSite = TryCreateExact(descriptor, itemType, callSiteChain, slot);
                        Debug.Assert(callSite != null);

                        callSites.Add(callSite);
                    }
                }
                else
                {
                    var slot = 0;
                    // We are going in reverse so the last service in descriptor list gets slot 0
                    for (var i = _descriptors.Count - 1; i >= 0; i--)
                    {
                        //遍历所有注册的ServiceDescriptor并获取对应的ServiceCallSite,然后如果不为空则添加至数组中
                        var descriptor = _descriptors[i];
                        var callSite = TryCreateExact(descriptor, itemType, callSiteChain, slot) ??
                                       TryCreateOpenGeneric(descriptor, itemType, callSiteChain, slot);
                        slot++;
                        if (callSite != null)
                        {
                            callSites.Add(callSite);
                        }
                    }
                    //      反转集合元素

                    callSites.Reverse();
                }

                //  实例化IEnumerableCallSite并返回
                return new IEnumerableCallSite(itemType, callSites.ToArray());
            }

            return null;
        }

        #endregion

        #region 根据注册服务的方式进行实例化

        /// <summary>
        /// 根据注册服务的方式进行实例化
        /// </summary>
        /// <param name="descriptor"></param>
        /// <param name="serviceType"></param>
        /// <param name="callSiteChain"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private ServiceCallSite TryCreateExact(ServiceDescriptor descriptor, Type serviceType,
            CallSiteChain callSiteChain, int slot)
        {
            //判断基类类型是否与ServiceDescriptor所持有的基类类型是否一致,如果不一致直接返回false
            if (serviceType == descriptor.ServiceType)
            {
                //根据当前注册的生命周期,基类类型和slot实例化一个ResultCache，ResultCache类型具有一个最后结果缓存的位置(相当于跟生命周期一致)和一个缓存Key
                ServiceCallSite callSite;
                var lifetime = new ResultCache(descriptor.Lifetime, serviceType, slot);

                //根据注册时所使用的方式来创建不同的ServiceCallSite,共具有三种ServiceCallSite子类

                if (descriptor.ImplementationInstance != null)
                {
                    //注册时直接根据对象进行实例化具体对象(Singleton生命周期独有)
                    callSite = new ConstantCallSite(descriptor.ServiceType, descriptor.ImplementationInstance);
                }
                else if (descriptor.ImplementationFactory != null)
                {
                    //FactoryCallSite     注册时根据一个工厂实例化对象
                    callSite = new FactoryCallSite(lifetime, descriptor.ServiceType, descriptor.ImplementationFactory);
                }
                else if (descriptor.ImplementationType != null)
                {
                    //ConstructorCallSite 注册时根据具体实例类型进行实例化对象，如果注册类型是使用的派生类类型方式,则调用CreateConstructorCallSite来实例化一个ConstructorCallSite
                    callSite = CreateConstructorCallSite(lifetime, descriptor.ServiceType,
                        descriptor.ImplementationType, callSiteChain);
                }
                else
                {
                    throw new InvalidOperationException("Invalid service descriptor");
                }

                return callSite;
            }

            return null;
        }

        #endregion

        /// <summary>
        /// 根据注册服务类型的泛型参数制造一个实现类型参数,然后调用CreateConstructorCallSite()进行实例化ServiceCallSite,所以泛型只能以构造器实例方式
        /// </summary>
        /// <param name="descriptor"></param>
        /// <param name="serviceType"></param>
        /// <param name="callSiteChain"></param>
        /// <param name="slot"></param>
        /// <returns></returns>
        private ServiceCallSite TryCreateOpenGeneric(ServiceDescriptor descriptor, Type serviceType,
            CallSiteChain callSiteChain, int slot)
        {
            // 如果当前泛型类型为封闭并且当前注册的基类类型为当前泛型的开放类型,则实例化,否则返回null
            if (serviceType.IsConstructedGenericType &&
                serviceType.GetGenericTypeDefinition() == descriptor.ServiceType)
            {
                Debug.Assert(descriptor.ImplementationType != null, "descriptor.ImplementationType != null");
                var lifetime = new ResultCache(descriptor.Lifetime, serviceType, slot); //利用当前注册服务的声明和生命周期类型实例化一个结果缓存配置
                var closedType =
                    descriptor.ImplementationType.MakeGenericType(serviceType
                        .GenericTypeArguments); //利用注册类型泛型参数创造派生类封闭泛型类型
                return CreateConstructorCallSite(lifetime, serviceType, closedType, callSiteChain);
            }

            return null;
        }

        #region 选择最优构造器并实例化ConstructorCallSite对象

        /// <summary>
        /// 选择最优构造器并实例化ConstructorCallSite对象,
        /// </summary>
        /// <param name="lifetime"></param>
        /// <param name="serviceType"></param>
        /// <param name="implementationType"></param>
        /// <param name="callSiteChain"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private ServiceCallSite CreateConstructorCallSite(ResultCache lifetime, Type serviceType,
            Type implementationType,
            CallSiteChain callSiteChain)
        {
            //将此服务类型和实例类型存入callSiteChain
            callSiteChain.Add(serviceType, implementationType);

            //获取实例类型的所有公共构造器,然后选择其最优的构造器并创建ConstructorCallSite  
            var constructors = implementationType.GetTypeInfo()
                .DeclaredConstructors
                .Where(constructor => constructor.IsPublic)
                .ToArray();

            ServiceCallSite[] parameterCallSites = null;

            if (constructors.Length == 0)
            {
                //首先获取实例类型的所有公共构造器,如果不存在就抛出异常
                throw new InvalidOperationException(Resources.FormatNoConstructorMatch(implementationType));
            }
            else if (constructors.Length == 1)
            {
                //如果此类型只有一个构造器,那么就使用此构造器当做最优构造器进行实例化
                var constructor = constructors[0];
                var parameters = constructor.GetParameters();
                if (parameters.Length == 0)
                {
                    return new ConstructorCallSite(lifetime, serviceType, constructor);
                }

                //如果此类型具有多个构造器,那么就选出最优构造器
                parameterCallSites = CreateArgumentCallSites(
                    serviceType,
                    implementationType,
                    callSiteChain,
                    parameters,
                    throwIfCallSiteNotFound: true);

                return new ConstructorCallSite(lifetime, serviceType, constructor, parameterCallSites);
            }

            // 如果没有找到最优构造器,就抛出异常,存在最优构造器就以此构造器实例化ConstructorCallSite
            Array.Sort(constructors,
                (a, b) => b.GetParameters().Length.CompareTo(a.GetParameters().Length));

            ConstructorInfo bestConstructor = null;
            HashSet<Type> bestConstructorParameterTypes = null;
            for (var i = 0; i < constructors.Length; i++)
            {
                var parameters = constructors[i].GetParameters();

                var currentParameterCallSites = CreateArgumentCallSites(
                    serviceType,
                    implementationType,
                    callSiteChain,
                    parameters,
                    throwIfCallSiteNotFound: false);

                if (currentParameterCallSites != null)
                {
                    if (bestConstructor == null)
                    {
                        bestConstructor = constructors[i];
                        parameterCallSites = currentParameterCallSites;
                    }
                    else
                    {
                        // Since we're visiting constructors in decreasing order of number of parameters,
                        // we'll only see ambiguities or supersets once we've seen a 'bestConstructor'.

                        if (bestConstructorParameterTypes == null)
                        {
                            bestConstructorParameterTypes = new HashSet<Type>(
                                bestConstructor.GetParameters().Select(p => p.ParameterType));
                        }

                        if (!bestConstructorParameterTypes.IsSupersetOf(parameters.Select(p => p.ParameterType)))
                        {
                            // Ambiguous match exception
                            var message = string.Join(
                                Environment.NewLine,
                                Resources.FormatAmbiguousConstructorException(implementationType),
                                bestConstructor,
                                constructors[i]);
                            throw new InvalidOperationException(message);
                        }
                    }
                }
            }

            if (bestConstructor == null)
            {
                throw new InvalidOperationException(
                    Resources.FormatUnableToActivateTypeException(implementationType));
            }
            else
            {
                Debug.Assert(parameterCallSites != null);
                return new ConstructorCallSite(lifetime, serviceType, bestConstructor, parameterCallSites);
            }
        }

        #endregion

        private ServiceCallSite[] CreateArgumentCallSites(
            Type serviceType,
            Type implementationType,
            CallSiteChain callSiteChain,
            ParameterInfo[] parameters,
            bool throwIfCallSiteNotFound)
        {
            var parameterCallSites = new ServiceCallSite[parameters.Length];
            for (var index = 0; index < parameters.Length; index++)
            {
                // 依次递归调用获取指定参数的ServiceCallSite
                var callSite = GetCallSite(parameters[index].ParameterType, callSiteChain);

                if (callSite == null &&
                    ParameterDefaultValue.TryGetDefaultValue(parameters[index], out var defaultValue))
                {
                    // 如果获取参数的ServiceCallSite失败但是该参数具有默认值
                    // 则直接以默认值来创建ConstantCallSite对象
                    callSite = new ConstantCallSite(serviceType, defaultValue);
                }

                // 如果当前callSite还为空,则代表出现无法实例化的参数类型
                // 如果允许抛出异常则抛出异常,如果不允许抛出异常则返回null
                if (callSite == null)
                {
                    if (throwIfCallSiteNotFound)
                    {
                        throw new InvalidOperationException(Resources.FormatCannotResolveService(
                            parameters[index].ParameterType,
                            implementationType));
                    }

                    return null;
                }

                parameterCallSites[index] = callSite;
            }

            return parameterCallSites;
        }


        /// <summary>
        /// 往_callSiteCache字段添加缓存ServiceCallSite
        /// </summary>
        /// <param name="type"></param>
        /// <param name="serviceCallSite"></param>
        public void Add(Type type, ServiceCallSite serviceCallSite)
        {
            _callSiteCache[type] = serviceCallSite;
        }

        private struct ServiceDescriptorCacheItem
        {
            /// <summary>
            /// 代表此注册服务的第一个ServiceDescriptor
            /// </summary>
            private ServiceDescriptor _item;

            /// <summary>
            /// 此字段表示除去第一个的的所有ServiceDescriptor集合
            /// </summary>
            private List<ServiceDescriptor> _items;

            public ServiceDescriptor Last
            {
                get
                {
                    if (_items != null && _items.Count > 0)
                    {
                        return _items[_items.Count - 1];
                    }

                    Debug.Assert(_item != null);
                    return _item;
                }
            }

            public int Count
            {
                get
                {
                    if (_item == null)
                    {
                        Debug.Assert(_items == null);
                        return 0;
                    }

                    return 1 + (_items?.Count ?? 0);
                }
            }

            public ServiceDescriptor this[int index]
            {
                get
                {
                    if (index >= Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    if (index == 0)
                    {
                        return _item;
                    }

                    return _items[index - 1];
                }
            }

            /// <summary>
            /// 将指定固定ServiceDescriptor添加到集合中
            /// 首先实例化一个新的 ServiceDescriptorCacheItem对象
            /// 如果当前对象_item属性为空,则将当前参数作为新ServiceDescriptorCacheItem对象>item属性
            /// 如果当前对象_item不为空,则当前的对象_item作为新ServiceDescriptorCacheItem对象>item属性,并且将原对象集合赋值给新对象集合,并且将参数加入到新对象集合中,然后返回新对象,
            /// 也就是第一个加入的永远是_item值,其后加入的放入集合中
            /// </summary>
            /// <param name="descriptor"></param>
            /// <returns></returns>
            public ServiceDescriptorCacheItem Add(ServiceDescriptor descriptor)
            {
                var newCacheItem = new ServiceDescriptorCacheItem();
                if (_item == null)
                {
                    Debug.Assert(_items == null);
                    newCacheItem._item = descriptor;
                }
                else
                {
                    newCacheItem._item = _item;
                    newCacheItem._items = _items ?? new List<ServiceDescriptor>();
                    newCacheItem._items.Add(descriptor);
                }

                return newCacheItem;
            }
        }
    }
}