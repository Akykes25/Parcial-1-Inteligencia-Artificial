using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Parcial1.Tests
{
    public class InterestObjectPoolTests
    {
        private GameObject bootstrapObject;
        private GameObject spawnedInterestObject;

        [TearDown]
        public void TearDown()
        {
            if (spawnedInterestObject != null)
            {
                UnityEngine.Object.DestroyImmediate(spawnedInterestObject);
            }

            if (bootstrapObject != null)
            {
                UnityEngine.Object.DestroyImmediate(bootstrapObject);
            }
        }

        [Test]
        public void ExhaustedInterestObject_IsReactivatedFromPool()
        {
            Type bootstrapType = FindRuntimeType("SimulationBootstrap");
            bootstrapObject = new GameObject("SimulationBootstrap_Test");
            Component bootstrap = bootstrapObject.AddComponent(bootstrapType);

            Component firstInterest = Invoke<Component>(bootstrap, "SpawnInterestObject");
            spawnedInterestObject = firstInterest.gameObject;
            bool receivedDamage = Invoke<bool>(firstInterest, "ReceiveDamage", 1000f);

            Assert.That(receivedDamage, Is.True);
            Assert.That(firstInterest.gameObject.activeSelf, Is.False);
            Assert.That(GetProperty<int>(bootstrap, "ActiveInterestCount"), Is.Zero);

            Component reusedInterest = Invoke<Component>(bootstrap, "SpawnInterestObject");

            Assert.That(reusedInterest, Is.SameAs(firstInterest));
            Assert.That(reusedInterest.gameObject.activeSelf, Is.True);
            Assert.That(GetProperty<bool>(reusedInterest, "IsAvailable"), Is.True);
            Assert.That(GetProperty<int>(bootstrap, "ActiveInterestCount"), Is.EqualTo(1));
            Assert.That(GetProperty<int>(bootstrap, "InterestObjectPoolSize"), Is.EqualTo(1));
        }

        private static Type FindRuntimeType(string typeName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(typeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            throw new InvalidOperationException("No se encontro el tipo runtime " + typeName + ".");
        }

        private static T GetProperty<T>(Component target, string propertyName)
        {
            const BindingFlags flags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            PropertyInfo property = target.GetType().GetProperty(propertyName, flags);
            Assert.That(property, Is.Not.Null, "No se encontro la propiedad " + propertyName + ".");
            return (T)property.GetValue(target);
        }

        private static T Invoke<T>(Component target, string methodName, params object[] arguments)
        {
            const BindingFlags flags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            MethodInfo method = target.GetType().GetMethod(methodName, flags);
            Assert.That(method, Is.Not.Null, "No se encontro el metodo " + methodName + ".");
            return (T)method.Invoke(target, arguments);
        }
    }
}
