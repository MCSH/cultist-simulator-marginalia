using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using SecretHistories.Fucine;
using SecretHistories.UI;

namespace MarginaliaMod
{
    // The Roost Machine moves some of the game's own properties into its own store as
    // content loads: a recipe's effects, mutations and deck draws go into a
    // "GrandEffects" object, and a card's xtriggers into its own list. Read through
    // reflection, so the mod neither needs Roost nor breaks without it.
    internal static class RoostCompat
    {
        private static bool _looked;
        private static MethodInfo _retrieve;

        public static bool Present
        {
            get
            {
                if (!_looked)
                {
                    _looked = true;
                    var machine = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Roost.Machine", false))
                        .FirstOrDefault(t => t != null);
                    // There's a generic overload with the same parameters, so pick by hand.
                    _retrieve = machine?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "RetrieveProperty" && !m.IsGenericMethodDefinition &&
                                             m.GetParameters().Select(p => p.ParameterType)
                                                 .SequenceEqual(new[] { typeof(IEntityWithId), typeof(string) }));
                    if (_retrieve != null)
                        NoonUtility.Log("Marginalia: The Roost Machine is loaded; reading recipe effects and xtriggers from it.");
                }
                return _retrieve != null;
            }
        }

        // One of Roost's custom properties on an entity, or null.
        public static object Property(IEntityWithId entity, string name)
        {
            if (entity == null || !Present)
                return null;
            try
            {
                return _retrieve.Invoke(null, new object[] { entity, name });
            }
            catch (Exception ex)
            {
                NoonUtility.Log("Marginalia: couldn't read Roost's '" + name + "' on " + entity.Id + ": " + ex.Message);
                return null;
            }
        }

        // A public property of one of Roost's objects, by name.
        public static object Get(object owner, string name) =>
            owner?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(owner);

        // An expression (FucineExp) as written in the content, e.g. "1" or "[~/sphere : lantern]".
        public static string Formula(object expression)
        {
            if (expression == null)
                return null;
            if (expression is string text)
                return text;
            var field = expression.GetType().GetField("formula", BindingFlags.Public | BindingFlags.Instance);
            return field != null ? field.GetValue(expression) as string : expression.ToString();
        }

        public static IDictionary Dictionary(object owner, string name) => Get(owner, name) as IDictionary;

        public static IList List(object owner, string name) => Get(owner, name) as IList;
    }
}
