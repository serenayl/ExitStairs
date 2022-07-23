using Elements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace ExitStairs
{
	/// <summary>
	/// Override metadata for StairsOverrideRemoval
	/// </summary>
	public partial class StairsOverrideRemoval : IOverride
	{
        public static string Name = "Stairs Removal";
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