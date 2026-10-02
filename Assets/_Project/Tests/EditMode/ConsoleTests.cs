using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VoidMall.Console;

namespace VoidMall.Tests
{
    public sealed class ConsoleTests
    {
        [Test]
        public void Tokenize_RespectsQuotes()
        {
            var tokens = ConsoleParser.Tokenize("echo \"hello world\"  42");
            Assert.That(tokens, Is.EqualTo(new[] { "echo", "hello world", "42" }));
        }

        [Test]
        public void SplitStatements_IgnoresSemicolonInQuotes()
        {
            var statements = ConsoleParser.SplitStatements("echo \"a;b\"; help");
            Assert.That(statements.Count, Is.EqualTo(2));
        }

        [Test]
        public void BindArguments_ParsesVectorAndDefaults()
        {
            var method = typeof(ConsoleTests).GetMethod(nameof(Sample), BindingFlags.Static | BindingFlags.NonPublic);
            var parameters = method!.GetParameters();
            bool ok = ConsoleParser.TryBindArguments(parameters, new[] { "cmd", "1", "2,5", "-3", "on" }, out var values, out var error);
            Assert.That(ok, Is.True, error);
            Assert.That(values[0], Is.EqualTo(new Vector3(1f, 2.5f, -3f)));
            Assert.That(values[1], Is.EqualTo(true));
            Assert.That(values[2], Is.EqualTo(7));
        }

        [Test]
        public void BindArguments_ReportsMissingArgument()
        {
            var method = typeof(ConsoleTests).GetMethod(nameof(Sample), BindingFlags.Static | BindingFlags.NonPublic);
            bool ok = ConsoleParser.TryBindArguments(method!.GetParameters(), new[] { "cmd", "1", "2" }, out _, out var error);
            Assert.That(ok, Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void CVar_ParsesAndRejects()
        {
            var cvar = new CVar<float>("test_value", 1f, "test");
            Assert.That(cvar.TrySet(new[] { "2.5" }, out _), Is.True);
            Assert.That(cvar.Value, Is.EqualTo(2.5f));
            Assert.That(cvar.TrySet(new[] { "abc" }, out _), Is.False);
            Assert.That(cvar.Value, Is.EqualTo(2.5f));
            cvar.ResetToDefault();
            Assert.That(cvar.Value, Is.EqualTo(1f));
        }

        [Test]
        public void CommonPrefix_IsCaseInsensitive()
        {
            Assert.That(DevConsole.CommonPrefix(new[] { "noclip", "NoTarget" }), Is.EqualTo("no"));
        }

        private static void Sample(Vector3 position, bool flag, int count = 7) { }
    }
}
