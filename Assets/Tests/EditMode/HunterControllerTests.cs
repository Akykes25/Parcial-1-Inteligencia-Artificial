using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Parcial1.Tests
{
    public class HunterControllerTests
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private GameObject hunterObject;
        private GameObject boidObject;
        private Component hunter;
        private Component boid;

        [SetUp]
        public void SetUp()
        {
            Type hunterType = FindRuntimeType("HunterController");
            Type boidType = FindRuntimeType("BoidAgent");

            hunterObject = new GameObject("Hunter_Test");
            hunterObject.transform.position = new Vector3(10000f, 10000f, 10000f);
            hunterObject.AddComponent<SphereCollider>();
            hunter = hunterObject.AddComponent(hunterType);
            InvokePrivate(hunter, "Awake");

            boidObject = new GameObject("Boid_Test");
            boidObject.transform.position = hunterObject.transform.position + Vector3.right;
            boidObject.AddComponent<SphereCollider>();
            boid = boidObject.AddComponent(boidType);
            InvokePrivate(boid, "Awake");

            SetPublicField(hunter, "TBA", 2f);
            SetPrivateField(hunter, "stateInitialized", true);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (boidObject != null)
            {
                UnityEngine.Object.DestroyImmediate(boidObject);
            }

            if (hunterObject != null)
            {
                UnityEngine.Object.DestroyImmediate(hunterObject);
            }

            Physics.SyncTransforms();
        }

        [Test]
        public void Initialize_UsesConfiguredTbaAsInitialCooldown()
        {
            SetPublicField(hunter, "TBA", 3.5f);

            InvokePrivate(hunter, "Initialize", null, new List<Transform>());

            Assert.That(
                GetProperty<float>(hunter, "TBARemaining"),
                Is.EqualTo(3.5f).Within(0.001f));
        }

        [Test]
        public void Patrol_EntersAttackOnlyWhenTbaIsReadyAndBoidIsVisible()
        {
            SetPrivateField(hunter, "tbaRemaining", 0.5f);

            InvokePrivate(hunter, "UpdatePatrol", 0f);

            Assert.That(GetProperty<string>(hunter, "CurrentStateName"), Is.EqualTo("Patrol"));
            Assert.That(GetProperty<object>(hunter, "CurrentTarget"), Is.Null);

            SetPrivateField(hunter, "tbaRemaining", 0f);
            InvokePrivate(hunter, "UpdatePatrol", 0f);

            Assert.That(GetProperty<string>(hunter, "CurrentStateName"), Is.EqualTo("Attack"));
            Assert.That(GetProperty<object>(hunter, "CurrentTarget"), Is.SameAs(boid));
        }

        [Test]
        public void SuccessfulAttack_ResetsTbaAndReturnsToStablePatrol()
        {
            SetPrivateField(hunter, "tbaRemaining", 0f);
            InvokePrivate(hunter, "UpdatePatrol", 0f);

            InvokePrivate(hunter, "UpdateAttack", 0f);

            Assert.That(GetProperty<string>(hunter, "CurrentStateName"), Is.EqualTo("Patrol"));
            Assert.That(GetProperty<int>(hunter, "AttackCount"), Is.EqualTo(1));
            Assert.That(GetProperty<float>(hunter, "TBARemaining"), Is.EqualTo(2f).Within(0.001f));
            Assert.That(GetProperty<float>(boid, "Health"), Is.EqualTo(66f).Within(0.001f));

            for (int i = 0; i < 5; i++)
            {
                InvokePrivate(hunter, "UpdatePatrol", 0.1f);
                Assert.That(GetProperty<string>(hunter, "CurrentStateName"), Is.EqualTo("Patrol"));
            }
        }

        [Test]
        public void TargetLeavingRealVision_ReturnsToPatrolWithoutResettingTba()
        {
            SetPrivateField(hunter, "tbaRemaining", 0f);
            InvokePrivate(hunter, "UpdatePatrol", 0f);
            Assert.That(GetProperty<string>(hunter, "CurrentStateName"), Is.EqualTo("Attack"));

            boidObject.transform.position = hunterObject.transform.position + Vector3.right * 30f;
            Physics.SyncTransforms();
            InvokePrivate(hunter, "UpdateAttack", 0f);

            Assert.That(GetProperty<string>(hunter, "CurrentStateName"), Is.EqualTo("Patrol"));
            Assert.That(GetProperty<int>(hunter, "AttackCount"), Is.Zero);
            Assert.That(GetProperty<float>(hunter, "TBARemaining"), Is.Zero.Within(0.001f));
        }

        [Test]
        public void LethalAttack_ResetsTbaAndTransitionsToGather()
        {
            SetPrivateField(hunter, "attackDamage", 200f);
            SetPrivateField(hunter, "tbaRemaining", 0f);
            InvokePrivate(hunter, "UpdatePatrol", 0f);

            InvokePrivate(hunter, "UpdateAttack", 0f);

            Assert.That(GetProperty<string>(hunter, "CurrentStateName"), Is.EqualTo("Gather"));
            Assert.That(GetProperty<int>(hunter, "AttackCount"), Is.EqualTo(1));
            Assert.That(GetProperty<float>(hunter, "TBARemaining"), Is.EqualTo(2f).Within(0.001f));
            Assert.That(GetProperty<bool>(boid, "IsDead"), Is.True);
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

        private static void SetPublicField(Component target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, InstanceMembers);
            Assert.That(field, Is.Not.Null, "No se encontro el campo " + fieldName + ".");
            field.SetValue(target, value);
        }

        private static void SetPrivateField(Component target, string fieldName, object value)
        {
            SetPublicField(target, fieldName, value);
        }

        private static T GetProperty<T>(Component target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, InstanceMembers);
            Assert.That(property, Is.Not.Null, "No se encontro la propiedad " + propertyName + ".");
            return (T)property.GetValue(target);
        }

        private static object InvokePrivate(Component target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, InstanceMembers);
            Assert.That(method, Is.Not.Null, "No se encontro el metodo " + methodName + ".");
            return method.Invoke(target, arguments);
        }
    }
}
