using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sponge2.Model
{
	public class Person : ProjectElement
	{
		public Person(string desiredOrExistingFolder, ComponentFile.Factory componentFileFactory)
			: base(desiredOrExistingFolder, componentFileFactory)
		{

		}

		protected override string ExtensionWithoutPeriod
		{
			get { return "person"; }
		}
	}
}