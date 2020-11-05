// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    /// <summary>
    /// 缓存实例对象时使用Key 
    /// </summary>
    internal struct ServiceCacheKey: IEquatable<ServiceCacheKey>
    {
        public static ServiceCacheKey Empty { get; } = new ServiceCacheKey(null, 0);

        /// <summary>
        /// 注册服务类型
        /// Type of service being cached
        /// </summary>
        public Type Type { get; }

        /// <summary>
        /// 以IEnumerable类型解析时服务的反向索引,默认实例0
        /// 相同Type时此值为++
        /// Reverse index of the service when resolved in <code>IEnumerable&lt;Type&gt;</code> where default instance gets slot 0.
        /// For example for service collection
        ///  IService Impl1
        ///  IService Impl2
        ///  IService Impl3
        /// We would get the following cache keys:
        ///  Impl1 2
        ///  Impl2 1
        ///  Impl3 0
        /// </summary>
        public int Slot { get; }

        public ServiceCacheKey(Type type, int slot)
        {
            Type = type;
            Slot = slot;
        }

        public bool Equals(ServiceCacheKey other)
        {
            return Type == other.Type && Slot == other.Slot;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Type.GetHashCode() * 397) ^ Slot;
            }
        }
    }
}