using Elements;
using System.Collections.Generic;

namespace ExitStairs
{
	/// <summary>
	/// Override metadata for StairOverridesOverride
	/// </summary>
	public partial class StairOverridesOverride : IOverride
	{
        public static string Name = "Stair Overrides";
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