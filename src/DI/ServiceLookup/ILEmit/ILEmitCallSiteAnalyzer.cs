// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    // This class walks the service call site tree and tries to calculate approximate
    // code size to avoid array resizings during IL generation
    // It also detects if lock is required for scoped services resolution
    internal sealed class ILEmitCallSiteAnalyzer : CallSiteVisitor<object, ILEmitCallSiteAnalysisResult>
    {
        private const int ConstructorILSize = 6;

        private const int ScopedILSize = 64;

        private const int ConstantILSize = 4;

        private const int ServiceProviderSize = 1;

        private const int FactoryILSize = 16;

        internal static ILEmitCallSiteAnalyzer Instance { get; } = new ILEmitCallSiteAnalyzer();

        protected override ILEmitCallSiteAnalysisResult VisitDisposeCache(ServiceCallSite transientCallSite, object argument) => VisitCallSiteMain(transientCallSite, argument);

        /// <summary>
        /// 使用反射方法实例化对象,并且如果构造函数不为空则获取所有参数的实例对象
        /// </summary>
        /// <param name="constructorCallSite"></param>
        /// <param name="argument"></param>
        /// <returns></returns>
        protected override ILEmitCallSiteAnalysisResult VisitConstructor(ConstructorCallSite constructorCallSite, object argument)
        {
            var result = new ILEmitCallSiteAnalysisResult(ConstructorILSize);
            foreach (var callSite in constructorCallSite.ParameterCallSites)
            {
                result = result.Add(VisitCallSite(callSite, argument));
            }
            return result;
        }

        protected override ILEmitCallSiteAnalysisResult VisitRootCache(ServiceCallSite singletonCallSite, object argument) => VisitCallSiteMain(singletonCallSite, argument);

        protected override ILEmitCallSiteAnalysisResult VisitScopeCache(ServiceCallSite scopedCallSite, object argument)
        {
            return new ILEmitCallSiteAnalysisResult(ScopedILSize, hasScope: true).Add(VisitCallSiteMain(scopedCallSite, argument));
        }
        
        /// <summary>
        /// 直接返回了RuntimeResolverContext封装的容器
        /// </summary>
        /// <param name="constantCallSite"></param>
        /// <param name="argument"></param>
        /// <returns></returns>
        protected override ILEmitCallSiteAnalysisResult VisitConstant(ConstantCallSite constantCallSite, object argument) => new ILEmitCallSiteAnalysisResult(ConstantILSize);

        protected override ILEmitCallSiteAnalysisResult VisitServiceProvider(ServiceProviderCallSite serviceProviderCallSite, object argument) => new ILEmitCallSiteAnalysisResult(ServiceProviderSize);

        /// <summary>
        /// 直接返回了RuntimeResolverContext封装的容器
        /// </summary>
        /// <param name="serviceScopeFactoryCallSite"></param>
        /// <param name="argument"></param>
        /// <returns></returns>
        protected override ILEmitCallSiteAnalysisResult VisitServiceScopeFactory(ServiceScopeFactoryCallSite serviceScopeFactoryCallSite, object argument) => new ILEmitCallSiteAnalysisResult(ConstantILSize);

        /// <summary>
        /// IEnumerableCallSite中ServiceCallSites集合的所有对象,并组装到一个数组进行返回
        /// </summary>
        /// <param name="enumerableCallSite"></param>
        /// <param name="argument"></param>
        /// <returns></returns>
        protected override ILEmitCallSiteAnalysisResult VisitIEnumerable(IEnumerableCallSite enumerableCallSite, object argument)
        {
            var result = new ILEmitCallSiteAnalysisResult(ConstructorILSize);
            foreach (var callSite in enumerableCallSite.ServiceCallSites)
            {
                result = result.Add(VisitCallSite(callSite, argument));
            }
            return result;
        }

        /// <summary>
        /// 调用了FactoryCallSite实例对象的工厂方法获取实例
        /// </summary>
        /// <param name="factoryCallSite"></param>
        /// <param name="argument"></param>
        /// <returns></returns>
        protected override ILEmitCallSiteAnalysisResult VisitFactory(FactoryCallSite factoryCallSite, object argument) => new ILEmitCallSiteAnalysisResult(FactoryILSize);

        public ILEmitCallSiteAnalysisResult CollectGenerationInfo(ServiceCallSite callSite) => VisitCallSite(callSite, null);
    }
}