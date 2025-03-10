﻿using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.Resources;
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

            await VerifyCS.VerifyAnalyzerAsync(test);
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

            await VerifyCS.VerifyAnalyzerAsync(test);
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

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(20, 21)
                .WithArguments("_f", "lockObject2", "lockObject1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
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

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task VariablesInDiffClassesWithAndWithoutLocks()
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

                void foo()
                {
                    _f = 4; 
                }
            }

            class SecondClass
            {
                int _f;

                void baz()
                {
                    lock (this) 
                        _f = 6; 
                }

                void abc()
                {
                    lock (this) 
                        _f = 8; 
                }

                void foo()
                {
                    _f = 4; 
                }
            }
        }";

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(16, 21)
                .WithArguments("_f", "1", "1");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(38, 21)
                .WithArguments("_f", "2", "1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
        }

        [TestMethod]
        public async Task MultiFilesWithoutLock()
        {
            var test = @"
        #line 1 ""File1.cs""
        using System;

        namespace TestProject
        {
            public class Class1
            {
                public int _f;
                object lockObject1 = new object();

                public void MethodA()
                {
                    lock (lockObject1)
                    {
                        _f = 5; 
                    }
                }
            }
        }

        #line 1 ""File2.cs""
        namespace TestProject
        {
            public class Class2
            {
                public void MethodB(Class1 obj)
                {
                    obj._f = 10; 
                }
            }
        }";

        var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
            .WithSpan(29, 25, 29, 27)
            .WithArguments("_f", "1", "1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task MultiFilesDiffLockObjects()
        {
            var test = @"
        #line 1 ""File1.cs""
        using System;

        namespace TestProject
        {
            public class Class1
            {
                public int _f;
                object lockObject1 = new object();

                public void MethodA()
                {
                    lock (lockObject1)
                    {
                        _f = 5; 
                    }
                }
            }
        }

        #line 1 ""File2.cs""
        namespace TestProject
        {
            public class Class2
            {
                public void MethodB(Class1 obj)
                {
                    lock (this)
                    {
                        obj._f = 15; 
                    }
                }
            }
        }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithSpan(31, 29, 31, 31)
                .WithArguments("_f", "this", "lockObject1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task DiffLockObjectsAndMissingLock()
        {
            var test = @"
        class DiffLockObjectsAndMissingLock
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
            
            void abc()
            {
                _f = 4; 
            }

        }";

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(20, 21)
                .WithArguments("_f", "lockObject2", "lockObject1");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(26, 17)
                .WithArguments("_f", "2", "1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
        }
    }
}
