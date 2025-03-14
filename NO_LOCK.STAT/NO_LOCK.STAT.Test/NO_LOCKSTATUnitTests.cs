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

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(7, 21) 
                .WithArguments("IsAlwaysCalledInsideLock this", "_f", "75", "no");

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

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(10, 21) 
                .WithArguments("VarUsedInDifferentContexts this", "_f", "75", "no");

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

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(20, 21)
                .WithArguments("DiffLockObjects.lockObject1", "_f", "80", "DiffLockObjects.lockObject2");

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

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(41, 21)
                .WithArguments("VariablesInDiffClasses.SecondClass this", "_f", "75", "no");

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
            public static class LockObjects
            {
                public static object LockObject1 = new object();
            }

            public class Class1
            {
                public int _f;

                public void MethodA()
                {
                    lock (LockObjects.LockObject1)
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
                    lock (LockObjects.LockObject1)
                    {
                        obj._f = 5; 
                    }

                    lock (LockObjects.LockObject1)
                    {
                        obj._f = 5; 
                    }
                }
            }
        }";

        var expected = VerifyCS.Diagnostic("NO_LOCKSTAT")
            .WithSpan(33, 25, 33, 27)
            .WithArguments("TestProject.LockObjects.LockObject1", "_f", "75", "no");

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
            public static class LockObjects
            {
                public static object LockObject1 = new object();
                public static object LockObject2 = new object();
            }

            public class Class1
            {
                public int _f;

                public void MethodA()
                {
                    lock (LockObjects.LockObject2)
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
                    lock (LockObjects.LockObject1)
                    {
                        obj._f = 15; 
                    }
                    
                    lock (LockObjects.LockObject1)
                    {
                        obj._f = 15; 
                    }
                    
                    lock (LockObjects.LockObject1)
                    {
                        obj._f = 15; 
                    }
                }
            }
        }";

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithSpan(21, 25, 21, 27)
                .WithArguments("TestProject.LockObjects.LockObject1", "_f", "75", "TestProject.LockObjects.LockObject2");

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

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(44, 21)
                .WithArguments("DiffLockObjectsAndMissingLock.lockObject1", "_f", "71", "DiffLockObjectsAndMissingLock.lockObject2");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(51, 17)
                .WithArguments("DiffLockObjectsAndMissingLock.lockObject1", "_f", "71", "no");

            var expected3 = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(14, 21)
                .WithArguments("DiffLockObjectsAndMissingLock.lockObject2", "_x", "75", "DiffLockObjectsAndMissingLock.lockObject1");

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

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(40, 21)
                .WithArguments("DiffLockObjectsAndMissingLock.lockObject1", "_f", "71", "DiffLockObjectsAndMissingLock.lockObject2");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(46, 17)
                .WithArguments("DiffLockObjectsAndMissingLock.lockObject1", "_f", "71", "no");

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

            var expected = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(34, 29)
                .WithArguments("SameLockObjectsInDiffClasses.FirstClass.lockObject1", "_f", "80", "SameLockObjectsInDiffClasses.SecondClass.lockObject1");

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

            var expected1 = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(44, 21)
                .WithArguments("DoubleLocks lockObject2", "_f", "75", "DoubleLocks lockObject1");

            var expected2 = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(52, 21)
                .WithArguments("DoubleLocks lockObject1", "_f", "75", "DoubleLocks lockObject2");

            var expected3 = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(69, 17)
                .WithArguments("DoubleLocks lockObject1", "_f", "75", "no");

            var expected4 = VerifyCS.Diagnostic("NO_LOCKSTAT")
                .WithLocation(69, 17)
                .WithArguments("DoubleLocks lockObject2", "_f", "75", "no");

            await VerifyCS.VerifyAnalyzerAsync(test, expected1, expected2, expected3, expected4);
        }
    }
}
