using Elements;
using Elements.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;
using Elements.Geometry.Solids;

namespace ExitStairs
{
    public static class ExitStairs
    {
        public static Material EnclosureMaterial = new Material("Stair Enclosure", new Color(1, 1, 1, 0.9));

        /// <summary>
        /// The ExitStairs function.
        /// </summary>
        /// <param name="model">The input model.</param>
        /// <param name="input">The arguments to the execution.</param>
        /// <returns>A ExitStairsOutputs instance containing computed results and the model with any new elements.</returns>
        public static ExitStairsOutputs Execute(Dictionary<string, Model> inputModels, ExitStairsInputs input)
        {
            var output = new ExitStairsOutputs();
            var floorProxies = OccupancyOverride.ContextProxies(inputModels).ToList();
            var occupancyOverrides = input.Overrides?.Occupancy == null ? new List<OccupancyOverride>() : input.Overrides.Occupancy;
            var proxiesWithOverrides = new List<(ElementProxy<Elements.Floor> proxy, OccupancyOverride ovd)>();
            if (floorProxies.Count() < 2)
            {
                return output;
            }

            Console.WriteLine($"Num floors: {floorProxies.Count()}");

            foreach (var floorProxy in floorProxies)
            {
                var floor = floorProxy.Element;
                var floorOverride = GetOverrideForFloor(floor, occupancyOverrides);
                floorProxy.AddOverrideValue(floorOverride.GetName(), floorOverride.Value);
                output.Model.AddElement(floorProxy);
                proxiesWithOverrides.Add((floorProxy, floorOverride));
            }
            if (input.Overrides?.Additions?.Stairs != null)
            {
                var numStairsTotal = input.Overrides?.Additions?.Stairs.Count;
                var maxLoad = proxiesWithOverrides.Max(fp => fp.ovd.Value.Occupancy);
                var minLoadPerStair = Math.Ceiling((double)maxLoad / (double)numStairsTotal);
                var maxElevationChange = GetMaxElevationChange(floorProxies, out var startElevationAtMaxElevationChange);
                var extrusionHeight = proxiesWithOverrides.Max(fp => fp.proxy.Element.Elevation + maxElevationChange + 1);
                var floorDepth = floorProxies.FirstOrDefault().Element.Thickness;

                Console.WriteLine($"Max elevation change: {maxElevationChange}");

                foreach (var stairOpt in input.Overrides.Additions.Stairs)
                {
                    // TODO: make override
                    var targetRiserHeight = 0.178;
                    var treadDepth = 0.279;

                    var numRisers = Math.Ceiling(maxElevationChange / targetRiserHeight);
                    var numTreads = numRisers - 2; // subtract 2 for the two landings
                    var realRiserHeight = maxElevationChange / numRisers;

                    // TODO: better flesh out but this is from 1005.3
                    var multiplier = 0.3;
                    var minTreadWidth = Units.InchesToMeters(multiplier * minLoadPerStair);
                    var realMinTreadWidth = Math.Max(minTreadWidth, 1.118); // 44 inches per 1011.2
                    var treadWidth = realMinTreadWidth;
                    var capacity = (int)Math.Floor(Units.MetersToInches(treadWidth / multiplier));

                    var landingDepth = realMinTreadWidth;
                    var realLandingDepth = Math.Max(realMinTreadWidth, 1.219); // 48 inches per 1011.6

                    var width = realMinTreadWidth * 2;

                    var landing = Polygon.Rectangle(new Vector3(0, 0, 0), new Vector3(width, landingDepth, 0));

                    var solidOps = new List<SolidOperation>();
                    var curves = new List<ModelCurve>();

                    curves.Add(new ModelCurve(landing));

                    var firstTreads = Math.Ceiling(numTreads / 2);
                    var length = realLandingDepth * 2 + treadDepth * firstTreads;
                    var stair = Polygon.Rectangle(new Vector3(0, 0, 0), new Vector3(treadWidth, treadDepth));

                    for (var i = 0; i < firstTreads; i++)
                    {
                        curves.Add(new ModelCurve(stair, null, new Transform(new Vector3(treadWidth, i * treadDepth + landingDepth, (i + 1) * realRiserHeight))));
                    }

                    curves.Add(new ModelCurve(landing, null, new Transform(new Vector3(0, firstTreads * treadDepth + landingDepth, (firstTreads + 1) * realRiserHeight))));

                    var secondTreads = Math.Floor(numTreads / 2);

                    for (var i = firstTreads + 1; i <= numTreads; i++)
                    {
                        curves.Add(new ModelCurve(stair, null, new Transform(new Vector3(0, (numTreads - i) * treadDepth + landingDepth, (i + 1) * realRiserHeight))));
                    }

                    if (secondTreads < firstTreads)
                    {
                        curves.Add(new ModelCurve(stair, null, new Transform(new Vector3(0, landingDepth, (numTreads + 2) * realRiserHeight))));
                    }
                    curves.Add(new ModelCurve(landing, null, new Transform(new Vector3(0, 0, (numTreads + 2) * realRiserHeight))));

                    var mass = new Mass(Polygon.Rectangle(new Vector3(), new Vector3(width, length)));

                    var footprint = Polygon.Rectangle(new Vector3(), new Vector3(width, length));

                    solidOps.Add(new Extrude(footprint, extrusionHeight, Vector3.ZAxis, false));

                    var representation = new Representation(solidOps);
                    var transform = new Transform(stairOpt.Value.Origin);

                    var exitStair = new ExitStair(footprint, capacity, minLoadPerStair, transform, EnclosureMaterial, representation);

                    output.Model.AddElement(exitStair);
                    output.Model.AddElements(curves.Select(c => {
                        c.Transform.Concatenate(transform);
                        c.Transform.Concatenate(new Transform(new Vector3(0, 0, startElevationAtMaxElevationChange)));
                        return c;
                    }));
                }
            }
            return output;
        }

        public static OccupancyOverride GetOverrideForFloor(Floor floor, System.Collections.Generic.IList<OccupancyOverride> overrides)
        {
            foreach (var ovd in overrides)
            {
                if (ovd.Identity != null && ovd.Identity.Transform.Equals(floor.Transform) && ovd.Identity.Profile.Perimeter.IsAlmostEqualTo(floor.Profile.Perimeter))
                {
                    return ovd;
                }
            }

            return new OccupancyOverride(Guid.NewGuid().ToString(), null, new OccupancyValue(100));
        }

        public static double GetMaxElevationChange(System.Collections.Generic.IList<ElementProxy<Floor>> floorProxies, out double startElevation)
        {
            var maxElevationChange = 0.0;
            var ordered = floorProxies.OrderBy(fp => fp.Element.Elevation).ToList();
            startElevation = ordered.FirstOrDefault().Element.Elevation;
            for (var i = 1; i < ordered.Count; i++)
            {
                var contender = floorProxies[i].Element.Elevation - floorProxies[i - 1].Element.Elevation;
                if (contender > maxElevationChange) {
                    startElevation = floorProxies[i - 1].Element.Elevation;
                    maxElevationChange = contender;
                }
            }
            return maxElevationChange;
        }
    }
}