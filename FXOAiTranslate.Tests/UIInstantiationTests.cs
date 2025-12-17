using System;
using System.Threading;
using NUnit.Framework;
using FXOAiTranslate.WPF.Views;
using FXOAiTranslate.WPF.Controls;

namespace FXOAiTranslate.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class UIInstantiationTests
    {
        [Test]
        public void CanInstantiate_FXAggregatorWindow()
        {
            Assert.DoesNotThrow(() =>
            {
                var window = new FXAggregatorWindow();
                Assert.That(window, Is.Not.Null);
            });
        }

        [Test]
        public void CanInstantiate_LegPanel()
        {
            Assert.DoesNotThrow(() =>
            {
                var control = new LegPanel();
                Assert.That(control, Is.Not.Null);
            });
        }

        [Test]
        public void CanInstantiate_LadderView()
        {
            Assert.DoesNotThrow(() =>
            {
                var control = new LadderView();
                Assert.That(control, Is.Not.Null);
            });
        }
        
        [Test]
        public void CanInstantiate_DealsPanel()
        {
            Assert.DoesNotThrow(() =>
            {
                var control = new DealsPanel();
                Assert.That(control, Is.Not.Null);
            });
        }
    }
}
