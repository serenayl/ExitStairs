using Elements;
using System.Collections.Generic;

namespace ExitStairs
{
	/// <summary>
	/// Override metadata for StairsOverride
	/// </summary>
	public partial class StairsOverride : IOverride
	{
        public static string Name = "Stairs";
        public static string Dependency = null;
        public static string Context = "[*discriminator=Elements.ExitStair]";
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

	}
}