using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TestDriven.Framework;
using Xunit.Abstractions;

namespace Xunit.Runner.TdNet
{
    public class TdNetRunnerHelper : IDisposable
    {
        readonly Stack<IDisposable> toDispose = new Stack<IDisposable>();
        readonly Xunit2 xunit;
        readonly ITestListener testListener;

        /// <summary>
        /// This constructor is for unit testing purposes only.
        /// </summary>
        protected TdNetRunnerHelper() { }

        public TdNetRunnerHelper(Assembly assembly, ITestListener testListener)
        {
            this.testListener = testListener;

            xunit = new Xunit2(new NullSourceInformationProvider(), assembly.GetLocalCodeBase());
            toDispose.Push(xunit);
        }

        public virtual IEnumerable<ITestCase> Discover()
        {
            return Discover(sink => xunit.Find(false, sink, new XunitDiscoveryOptions()));
        }

        private IEnumerable<ITestCase> Discover(Type type)
        {
            return Discover(sink => xunit.Find(type.FullName, false, sink, new XunitDiscoveryOptions()));
        }

        private IEnumerable<ITestCase> Discover(Action<IMessageSink> discoveryAction)
        {
            try
            {
                var visitor = new TestDiscoveryVisitor();
                toDispose.Push(visitor);
                discoveryAction(visitor);
                visitor.Finished.WaitOne();
                return visitor.TestCases.ToList();
            }
            catch (Exception ex)
            {
                testListener.WriteLine("Error during test discovery:\r\n" + ex, Category.Error);
                return Enumerable.Empty<ITestCase>();
            }
        }

        public void Dispose()
        {
            foreach (var disposable in toDispose)
                disposable.Dispose();
        }

        public virtual TestRunState Run(IEnumerable<ITestCase> testCases = null, TestRunState initialRunState = TestRunState.NoTests)
        {
            try
            {
                if (testCases != null)
                    testCases = testCases.ToList();

                var visitor = new ResultVisitor(testListener) { TestRunState = initialRunState };
                toDispose.Push(visitor);

                if (testCases == null)
                    xunit.Run(visitor, new XunitDiscoveryOptions(), new XunitExecutionOptions());
                else
                    xunit.Run(testCases, visitor, new XunitExecutionOptions());

                visitor.Finished.WaitOne();

                return visitor.TestRunState;
            }
            catch (Exception ex)
            {
                testListener.WriteLine("Error during test execution:\r\n" + ex, Category.Error);
                return TestRunState.Error;
            }
        }

        public virtual TestRunState RunClass(Type type, TestRunState initialRunState = TestRunState.NoTests)
        {
            var state = Run(Discover(type), initialRunState);

            foreach (MemberInfo memberInfo in type.GetMembers())
            {
                Type childType = memberInfo as Type;
                if (childType != null)
                    state = RunClass(childType, state);
            }

            return state;
        }

        public virtual TestRunState RunMethod(MethodInfo method, TestRunState initialRunState = TestRunState.NoTests)
        {
            var testCases = Discover(method.ReflectedType).Where(tc =>
            {
                var methodInfo = tc.GetMethod();
                if (methodInfo == method)
                    return true;

                if (methodInfo.IsGenericMethod)
                    return methodInfo.GetGenericMethodDefinition() == method;

                return false;
            }).ToList();

            return Run(testCases, initialRunState);
        }
    }
}