using Elements;
using System.Collections.Generic;

namespace ExitStairs
{
	/// <summary>
	/// Override metadata for OccupancyOverride
	/// </summary>
	public partial class OccupancyOverride : IOverride
	{
        public static string Name = "Occupancy";
        public static string Dependency = "Floors";
        public static string Context = "[*discriminator=Elements.Floor]";
		public static string Paradigm = "Edit";

        /// <summary>
        /// Get the override name for this override.
        /// </summary>
        public string GetName() {
			return Name;
		}

		public object GetIdentity() {

			return Identity;
		}

		/// <summary>
		/// Get context elements that are applicable to this override.
		/// </summary>
		/// <param name="models">Dictionary of input models, or any other kind of dictionary of models.</param>
		/// <returns>List of context elements that match what is defined on the override.</returns>
		public static IEnumerable<ElementProxy<Elements.Floor>> ContextProxies(Dictionary<string, Model> models) {
			return models.AllElementsOfType<Elements.Floor>(Dependency).Proxies(Dependency);
		}
	}
}