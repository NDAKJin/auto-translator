using System;
using System.Threading.Tasks;
using Xunit;

namespace AutoTranslator.Tests;

public class ObfuscationTests
{


    private void SynchronousMethod()
    {
        while (true)
        {
            Console.WriteLine("Hello, World!");
        }

    }
    private async Task AsyncMethod()
    {
        SynchronousMethod();
    }
    private async void AsyncMethodCaller()
    {
        await AsyncMethod();
        Console.WriteLine("finish!");

    }
    [Fact]
    public void TestasyncMethod()
    {
        AsyncMethodCaller();
    }
}
