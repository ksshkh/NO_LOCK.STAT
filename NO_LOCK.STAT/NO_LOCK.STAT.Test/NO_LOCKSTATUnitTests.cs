using Microsoft.CodeAnalysis;
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

                void abc()
                {
                    lock (this) 
                        _f = 9; 
                }
            }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(7, 21) 
                .WithArguments("_f", "75", "this", "3", "1");

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
                    lock (this)
                    {
                        _f = i; 
                    }
                }
            }

            void bar()
            {
                lock (this)
                {
                    _f = 5; 
                }
            }

            void baz()
            {
                lock (this)
                {
                    _f = 5; 
                }
            }
        }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(10, 21) 
                .WithArguments("_f", "75", "this", "3", "1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
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

                lock (lockObject1)
                {
                    _f = 5; 
                }

                lock (lockObject1)
                {
                    _f = 5; 
                }

                lock (lockObject1)
                {
                    _f = 5; 
                }
            }
        }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(20, 21)
                .WithArguments("_f", "80", "lockObject1", "lockObject2");

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

                void foo()
                {
                    _f = 4; 
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

                    lock (this) 
                        _f = 9;
                }

                void foo()
                {
                    _f = 4; 
                }
            }
        }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(41, 21)
                .WithArguments("_f", "75", "this", "3", "1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
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

                public void MethodA()
                {
                    lock (this)
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

                public void MethodC(Class1 obj)
                {
                    lock (this)
                    {
                        obj._f = 5; 
                    }

                    lock (this)
                    {
                        obj._f = 5; 
                    }
                }
            }
        }";

        var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
            .WithSpan(28, 25, 28, 27)
            .WithArguments("_f", "75", "this", "3", "1");

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

                public void MethodA()
                {
                    lock (this)
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
                object lockObject1 = new object();

                public void MethodB(Class1 obj)
                {
                    lock (lockObject1)
                    {
                        obj._f = 15; 
                    }
                    
                    lock (this)
                    {
                        obj._f = 15; 
                    }
                    
                    lock (this)
                    {
                        obj._f = 15; 
                    }
                }
            }
        }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithSpan(32, 29, 32, 31)
                .WithArguments("_f", "75", "this", "lockObject1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task DiffLockObjectsAndMissingLock1()
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

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
        public async Task DiffLockObjectsAndMissingLock2()
        {
            var test = @"
        class DiffLockObjectsAndMissingLock
        {
            object lockObject1 = new object();
            object lockObject2 = new object();
            int _f;
            int _x;

            void foo()
            {
                lock (lockObject1)
                {
                    _f = 4;
                    _x = 9;
                }

                lock (lockObject2)
                {
                    _x = 7; 
                }

                lock (lockObject2)
                {
                    _x = 7; 
                }

                lock (lockObject1)
                {
                    _f = 4; 
                    _f = 9;
                }

                lock (lockObject1)
                {
                    _f = 4;
                    _f = 6;
                }
            }

            void bar()
            {
                lock (lockObject2)
                {
                    _f = 5; 
                    _x = 9;
                }
            }
            
            void abc()
            {
                _f = 4; 
            }

        }";

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(44, 21)
                .WithArguments("_f", "71", "lockObject1", "lockObject2");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(51, 17)
                .WithArguments("_f", "71", "lockObject1", "5", "2");

            var expected3 = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(14, 21)
                .WithArguments("_x", "75", "lockObject2", "lockObject1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected3, expected1, expected2);
        }

        [TestMethod]
        public async Task DiffLockObjectsAndMissingLock3()
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

                lock (lockObject1)
                {
                    _f = 4; 
                }

                lock (lockObject1)
                {
                    _f = 4; 
                }

                lock (lockObject1)
                {
                    _f = 4; 
                }

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
                .WithLocation(40, 21)
                .WithArguments("_f", "71", "lockObject1", "lockObject2");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(46, 17)
                .WithArguments("_f", "71", "lockObject1", "5", "2");

            await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2);
        }

        [TestMethod]
        public async Task SameLockObjectsInDiffClasses()
        {
            var test = @"
        namespace SameLockObjectsInDiffClasses
        {
            class FirstClass
            {
                object lockObject1 = new object();
                public int _f;

                void baz()
                {
                    lock (lockObject1) 
                        _f = 6; 
                }

                void foo()
                {
                    lock (lockObject1) 
                        _f = 6;

                    lock (lockObject1) 
                        _f = 6;

                    lock (lockObject1) 
                        _f = 6;
                }
            }

            class SecondClass
            {
                object lockObject1 = new object();
                void baz(FirstClass obj)
                {
                    lock (lockObject1) 
                        obj._f = 6; 
                }
            }
        }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(34, 29)
                .WithArguments("_f", "80", "lockObject1", "lockObject1");

            await VerifyCS.VerifyAnalyzerAsync(test, expected);
        }

        [TestMethod]
        public async Task DoubleLocks()
        {
            var test = @"
        class DoubleLocks
        {
            object lockObject1 = new object();
            object lockObject2 = new object();
            int _f;

            void foo()
            {
                lock (lockObject1)
                {
                    lock (lockObject2)
                    {
                        _f = 4; 
                    }
                }

                lock (lockObject1)
                {
                    lock (lockObject2)
                    {
                        _f = 4; 
                    }
                }

                lock (lockObject1)
                {
                    lock (lockObject2)
                    {
                        _f = 4; 
                    }
                }

                lock (lockObject2)
                {
                    lock (lockObject1)
                    {
                        _f = 4; 
                    }
                }

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

            void baz()
            {
                lock (lockObject2)
                {
                    lock (lockObject1)
                    {
                        _f = 4; 
                    }
                }
            }
            
            void abc()
            {
                _f = 4; 
            }

        }";

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(44, 21)
                .WithArguments("_f", "75", "lockObject2", "lockObject1");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT_DO")
                .WithLocation(52, 21)
                .WithArguments("_f", "75", "lockObject1", "lockObject2");

            var expected3 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(69, 17)
                .WithArguments("_f", "75", "lockObject1", "6", "2");

            var expected4 = VerifyCS.Diagnostic("NO_LOCKSTAT_ML")
                .WithLocation(69, 17)
                .WithArguments("_f", "75", "lockObject2", "6", "2");

            await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2, expected3, expected4);
        }
    }
}
