using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS = NO_LOCK.STAT.Test.CSharpCodeFixVerifier<
    NO_LOCK.STAT.NO_LOCKSTATAnalyzer,
    NO_LOCK.STAT.NO_LOCKSTATCodeFixProvider>;

namespace NO_LOCK.STAT.Test
{
    [TestClass]
    public class NO_LOCKSTATUnitTest
    {
        //No diagnostics expected to show up
        [TestMethod]
        public async Task TestMethod1()
        {
            var test = @"";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task IsAlwaysCalledInsideLock()
        {
            var test = @"
        class IsAlwaysCalledInsideLock
            {
                int _f;
                void foo()
                {
                    _f = 4;
                }

                void bar()
                {
                    lock (this) 
                        _f = 5; 
                }

                void baz()
                {
                    lock (this) 
                        _f = 6; 
                }
            }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(7, 21) 
                .WithArguments("_f", "2", "1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task VarCalledOnlyInLock()
        {
            var test = @"
        class IsAlwaysCalledInsideLock
        {
            int _f;
            int _x;

            void bar()
            {
                lock (this)
                {
                    _f = 5;
                    _x = 10;
                } 
            }

            void baz()
            {
                lock (this) 
                    _f = 6; 
            }

        }";

            await VerifyCS.VerifyAnalyzerAsync(test); 
        }

        [TestMethod]
        public async Task VarUsedOnlyOutsideLock()
        {
            var test = @"
        class VarUsedOnlyOutsideLock
        {
            int _f;

            void foo()
            {
                _f = 4; 
            }
        }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(8, 17) 
                .WithArguments("_f", "0", "1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task VarUsedOutsideLockManyTimes()
        {
            var test = @"
        class VarUsedOutsideLockManyTimes
        {
            int _f;

            void Foo()
            {
                _f = 4; 
                _f = 5; 
            }
        }";

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(8, 17) 
                .WithArguments("_f", "0", "2");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(9, 17) 
                .WithArguments("_f", "0", "2");

            await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
        }

        [TestMethod]
        public async Task VarUsedInDifferentContexts()
        {
            var test = @"
        class VarUsedInDifferentContexts
        {
            int _f;

            void foo()
            {
                if (true)
                {
                    _f = 4; 
                }

                for (int i = 0; i < 10; i++)
                {
                    _f = i; 
                }
            }

            void bar()
            {
                lock (this)
                {
                    _f = 5; 
                }
            }
        }";

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(10, 21) 
                .WithArguments("_f", "1", "2");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(15, 21) 
                .WithArguments("_f", "1", "2");

            await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
        }

        [TestMethod]
        public async Task DiffLockObjects()
        {
            var test = @"
        class DiffLockObjects
        {
            object lockObject1 = new object();
            object lockObject2 = new object();
            int _f;

            void foo()
            {
                lock (lockObject1)
                {
                    _f = 4; 
                }
            }

            void bar()
            {
                lock (lockObject2)
                {
                    _f = 5; 
                }
            }
        }";

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(12, 21)
                .WithArguments("_f", "lockObject1", "lockObject2");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(20, 21)
                .WithArguments("_f", "lockObject2", "lockObject1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
        }

        [TestMethod]
        public async Task VariablesInDiffClasses()
        {
            var test = @"
        namespace VariablesInDiffClasses
        {
            class FirstClass
            {
                int _f;

                void baz()
                {
                    lock (this) 
                        _f = 6; 
                }
            }

            class SecondClass
            {
                int _f;

                void foo()
                {
                    _f = 4; 
                }
            }
        }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(21, 21) 
                .WithArguments("_f", "0", "1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }
    }
}
