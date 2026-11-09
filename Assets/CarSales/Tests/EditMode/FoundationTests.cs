using System;
using CarSales.Foundation;
using NUnit.Framework;

namespace CarSales.Tests
{
    public class EventBusTests
    {
        struct Ping { public int Value; }
        struct Other { }

        [SetUp] public void SetUp() => EventBus.Clear();

        [Test]
        public void Publish_ReachesSubscribers_OfThatTypeOnly()
        {
            int got = 0, other = 0;
            EventBus.Subscribe<Ping>(p => got += p.Value);
            EventBus.Subscribe<Other>(_ => other++);
            EventBus.Publish(new Ping { Value = 5 });
            Assert.AreEqual(5, got);
            Assert.AreEqual(0, other);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            int got = 0;
            Action<Ping> h = p => got++;
            EventBus.Subscribe(h);
            EventBus.Unsubscribe(h);
            EventBus.Publish(new Ping());
            Assert.AreEqual(0, got);
        }

        [Test]
        public void ThrowingHandler_DoesNotStopOthers()
        {
            int got = 0;
            Exception reported = null;
            EventBus.OnHandlerError = e => reported = e;
            EventBus.Subscribe<Ping>(_ => throw new InvalidOperationException("boom"));
            EventBus.Subscribe<Ping>(_ => got++);
            EventBus.Publish(new Ping());
            EventBus.OnHandlerError = null;
            Assert.AreEqual(1, got);
            Assert.IsInstanceOf<InvalidOperationException>(reported);
        }

        [Test]
        public void Clear_RemovesAllSubscriptions()
        {
            EventBus.Subscribe<Ping>(_ => { });
            Assert.IsTrue(EventBus.HasSubscribers<Ping>());
            EventBus.Clear();
            Assert.IsFalse(EventBus.HasSubscribers<Ping>());
        }
    }

    public class GameCalendarTests
    {
        [TestCase(1, Weekday.Monday)]
        [TestCase(6, Weekday.Saturday)]
        [TestCase(7, Weekday.Sunday)]
        [TestCase(8, Weekday.Monday)]
        public void WeekdayOf(int day, Weekday expected) => Assert.AreEqual(expected, GameCalendar.WeekdayOf(day));

        [Test]
        public void WeeksAndQuarters()
        {
            Assert.AreEqual(1, GameCalendar.WeekOf(7));
            Assert.AreEqual(2, GameCalendar.WeekOf(8));
            Assert.IsTrue(GameCalendar.IsLastDayOfWeek(14));
            Assert.AreEqual(1, GameCalendar.QuarterOf(91));
            Assert.AreEqual(2, GameCalendar.QuarterOf(92));
            Assert.IsTrue(GameCalendar.IsLastDayOfQuarter(91));
            Assert.AreEqual(2, GameCalendar.YearOf(365));
            Assert.AreEqual(1, GameCalendar.QuarterInYear(365));
            Assert.IsTrue(GameCalendar.IsWeekend(13));
            Assert.IsFalse(GameCalendar.IsWeekend(12));
        }

        [Test]
        public void InvalidDays_AreTreatedAsDayOne()
        {
            Assert.AreEqual(Weekday.Monday, GameCalendar.WeekdayOf(0));
            Assert.AreEqual(1, GameCalendar.WeekOf(-5));
        }
    }

    public class MiniJsonTests
    {
        [Test]
        public void Parses_AllValueKinds()
        {
            var n = MiniJson.Parse("{ \"a\": 1.5, \"b\": [true, false, null], \"c\": \"x\\ny\\u0041\", \"d\": {} }");
            Assert.AreEqual(1.5, n["a"].Num);
            Assert.AreEqual(3, n["b"].Items.Count);
            Assert.AreEqual(JsonKind.Null, n["b"].Items[2].Kind);
            Assert.AreEqual("x\nyA", n["c"].Str);
            Assert.AreEqual(JsonKind.Object, n["d"].Kind);
            Assert.IsNull(n["missing"]);
        }

        [Test]
        public void Raw_ReturnsExactSourceText()
        {
            string inner = "{\n  \"cars\": [ {\"id\": \"a\"} ],\n  \"money\": -5 }";
            var n = MiniJson.Parse("{\"data\": " + inner + "}");
            Assert.AreEqual(inner, n["data"].Raw);
        }

        [TestCase("")]
        [TestCase("{")]
        [TestCase("{\"a\" 1}")]
        [TestCase("[1,]")]
        [TestCase("{\"a\": tru}")]
        [TestCase("{} extra")]
        [TestCase("\"unterminated")]
        public void Rejects_InvalidJson(string text) => Assert.Throws<FormatException>(() => MiniJson.Parse(text));

        [Test]
        public void Accepts_UnityNonFiniteFloats()
        {
            var n = MiniJson.Parse("{\"a\": NaN, \"b\": Infinity, \"c\": -Infinity, \"d\": -2.5e-3}");
            Assert.IsTrue(double.IsNaN(n["a"].Num));
            Assert.IsTrue(double.IsPositiveInfinity(n["b"].Num));
            Assert.IsTrue(double.IsNegativeInfinity(n["c"].Num));
            Assert.AreEqual(-0.0025, n["d"].Num, 1e-12);
            Assert.AreEqual(7, n["a"].AsInt(7), "non-finite numbers fall back");
        }

        [Test]
        public void Quote_RoundTripsSpecialCharacters()
        {
            string s = "He said \"hi\"\\ \n\t\u0001 ok";
            Assert.AreEqual(s, MiniJson.Parse(MiniJson.Quote(s)).Str);
        }
    }

    public class SaveMigrationTests
    {
        [Test]
        public void Migrate_RunsStepsInOrder()
        {
            var m = new SaveMigrations();
            m.Register("game", 1, d => d + "+2");
            m.Register("game", 2, d => d + "+3");
            var module = new SaveModule("game", 1, "v1");
            m.Migrate(module, 3);
            Assert.AreEqual("v1+2+3", module.Data);
            Assert.AreEqual(3, module.Version);
        }

        [Test]
        public void Migrate_PassesThroughVersionsWithoutSteps()
        {
            var m = new SaveMigrations();
            var module = new SaveModule("game", 4, "{}");
            m.Migrate(module, 5);
            Assert.AreEqual(5, module.Version);
            Assert.AreEqual("{}", module.Data);
        }

        [Test]
        public void Migrate_RefusesNewerSaves()
        {
            var m = new SaveMigrations();
            Assert.Throws<InvalidOperationException>(() => m.Migrate(new SaveModule("game", 9, "{}"), 5));
        }
    }
}
