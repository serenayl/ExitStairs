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

            // Get info about the floors of the building
            var floorProxies = OccupancyOverride.ContextProxies(inputModels).ToList();
            var occupancyOverrides = input.Overrides?.Occupancy == null ? new List<OccupancyOverride>() : input.Overrides.Occupancy;
            var proxiesWithOverrides = new List<(ElementProxy<Elements.Floor> proxy, OccupancyOverride ovd)>();
            if (floorProxies.Count() < 2)
            {
                return output;
            }

            // Figure out appropriate override/input values per floor
            foreach (var floorProxy in floorProxies)
            {
                var floor = floorProxy.Element;
                var floorOverride = GetOverrideForFloor(floor, occupancyOverrides);
                floorProxy.AddOverrideValue(floorOverride.GetName(), floorOverride.Value);
                output.Model.AddElement(floorProxy);
                proxiesWithOverrides.Add((floorProxy, floorOverride));
            }

            var globals = new StairConfig(input, proxiesWithOverrides);

            var stairPairs = new List<(ExitStair stair, StairConfig config)>();

            // Create starter stair
            var first = proxiesWithOverrides.First();
            var firstStairTransform = first.proxy.Element.Transform.Moved(first.proxy.Element.Profile.Perimeter.Centroid());
            stairPairs.Add((MakeStair(globals, firstStairTransform), globals));

            // Add manual stairs
            if (input.Overrides?.Additions?.Stairs != null)
            {
                foreach (var addOpt in input.Overrides.Additions.Stairs)
                {
                    var transform = new Transform(addOpt.Value.Origin);
                    var stair = MakeStair(globals, transform);
                    stair.AddOverrideIdentity(addOpt);
                    stairPairs.Add((stair, globals));
                }
            }

            // Apply positioning overrides
            if (input.Overrides?.Stairs != null)
            {
                foreach (var moveOpt in input.Overrides.Stairs)
                {
                    var matchingStair = GetMatchingStair(stairPairs, moveOpt.Identity.OriginalPosition);
                    if (matchingStair.stair != null)
                    {
                        matchingStair.stair.Transform = moveOpt.Value.Transform;
                        matchingStair.stair.AddOverrideIdentity(moveOpt);
                    }
                }
            }

            // Apply property overrides
            if (input.Overrides?.StairOverrides != null)
            {
                foreach (var propertyOpt in input.Overrides.StairOverrides)
                {
                    var matchingStair = GetMatchingStair(stairPairs, propertyOpt.Identity.OriginalPosition);
                    if (matchingStair.stair != null)
                    {
                        var newConfig = new StairConfig(globals, propertyOpt);
                        UpdateExitStair(matchingStair.stair, newConfig);
                        matchingStair.config = newConfig;
                        matchingStair.stair.Name = propertyOpt.Value.Name;
                        matchingStair.stair.AddOverrideIdentity(propertyOpt);
                    }
                }
            }

            // Delete stairs
            if (input.Overrides?.Removals?.Stairs != null)
            {
                foreach (var removalOpt in input.Overrides.Removals.Stairs)
                {
                    var matchingStair = GetMatchingStair(stairPairs, removalOpt.Identity.OriginalPosition);
                    if (matchingStair.stair != null)
                    {
                        stairPairs.Remove(matchingStair);
                        matchingStair.stair.AddOverrideIdentity(removalOpt);
                    }
                }
            }

            foreach (var stairPair in stairPairs) {
                output.Model.AddElement(stairPair.stair);
                output.Model.AddElements(GetStairExtraVisualization(stairPair.config, stairPair.stair));
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

        public static ExitStair MakeStair(StairConfig g, Transform transform)
        {
            var stair = new ExitStair(null, 0, 0, transform, EnclosureMaterial, null);
            stair.AdditionalProperties.Add("OriginalPosition", transform.Origin);
            UpdateExitStair(stair, g);
            return stair;
        }

        public static ExitStair UpdateExitStair(ExitStair stair, StairConfig c)
        {
            var solidOps = new List<SolidOperation>();
            var footprint = Polygon.Rectangle(new Vector3(), new Vector3(c.width, c.length));
            solidOps.Add(new Extrude(footprint, c.extrusionHeight, Vector3.ZAxis, false));
            var representation = new Representation(solidOps);
            stair.Boundary = footprint;
            stair.Representation = representation;
            stair.Capacity = c.capacity;
            stair.Load = c.minLoadPerStair;

            if (stair.AdditionalProperties.TryGetValue("Minimum Tread Width", out var _))
            {
                stair.AdditionalProperties["Minimum Tread Width"] = c.minimumWidth;
            }
            else
            {
                stair.AdditionalProperties.Add("Minimum Tread Width", c.minimumWidth);
            }

            return stair;
        }

        public static Vector3 GetOriginalPosition(ExitStair stair)
        {
            if (stair.AdditionalProperties.TryGetValue("OriginalPosition", out var originalPosition))
            {
                return (Vector3)originalPosition;
            }
            return new Vector3(-Int32.MaxValue, -Int32.MaxValue, -Int32.MaxValue);
        }

        public static (ExitStair stair, StairConfig config) GetMatchingStair(List<(ExitStair stair, StairConfig config)> stairs, Vector3 overrideOriginalPosition)
        {
            return stairs.FirstOrDefault(s =>
            {
                var closeEnough = GetOriginalPosition(s.stair).IsAlmostEqualTo(overrideOriginalPosition);
                return closeEnough;
            });
        }

        public static List<ModelCurve> GetStairExtraVisualization(StairConfig g, ExitStair exitStair)
        {
            var curves = new List<ModelCurve>();

            // Entry landing
            var landing = Polygon.Rectangle(new Vector3(0, 0, 0), new Vector3(g.width, g.landingDepth, 0));
            curves.Add(new ModelCurve(landing));

            // Single stair tread
            var tread = Polygon.Rectangle(new Vector3(0, 0, 0), new Vector3(g.treadWidth, g.treadDepth));

            // First flight
            for (var i = 0; i < g.firstTreads; i++)
            {
                curves.Add(new ModelCurve(tread, null, new Transform(new Vector3(g.treadWidth, i * g.treadDepth + g.landingDepth, (i + 1) * g.realRiserHeight))));
            }

            // Middle landing
            curves.Add(new ModelCurve(landing, null, new Transform(new Vector3(0, g.firstTreads * g.treadDepth + g.landingDepth, (g.firstTreads + 1) * g.realRiserHeight))));

            // Second flight
            for (var i = g.firstTreads + 1; i <= g.numTreads; i++)
            {
                curves.Add(new ModelCurve(tread, null, new Transform(new Vector3(0, (g.numTreads - i) * g.treadDepth + g.landingDepth, (i + 1) * g.realRiserHeight))));
            }

            // Last stair on second flight if it required one less step than the first flight
            if (g.secondTreads < g.firstTreads)
            {
                curves.Add(new ModelCurve(tread, null, new Transform(new Vector3(0, g.landingDepth, (g.numTreads + 2) * g.realRiserHeight))));
            }

            // Landing above starting one
            curves.Add(new ModelCurve(landing, null, new Transform(new Vector3(0, 0, (g.numTreads + 2) * g.realRiserHeight))));

            return curves.Select(c =>
            {
                c.Transform.Concatenate(exitStair.Transform);
                c.Transform.Concatenate(new Transform(new Vector3(0, 0, g.startElevationAtMaxElevationChange)));
                return c;
            }).ToList();
        }
    }

    /** Calculate configs for a stair */
    public class StairConfig
    {
        public double extrusionHeight;
        public double firstTreads;
        public double floorDepth;
        public double landingDepth;
        public double length;
        public double maxElevationChange;
        public double minimumWidth = Units.InchesToMeters(44); // per 1011.2
        public double minTreadWidth;
        public double realLandingDepth;
        public double realMinTreadWidth;
        public double realRiserHeight;
        public double secondTreads;
        public double startElevationAtMaxElevationChange;
        public double targetRiserHeight = Units.InchesToMeters(7); // TODO: make override? maybe not necessary
        public double treadDepth = Units.InchesToMeters(11); // TODO: make override? maybe not necessary
        public double treadWidth;
        public double width;
        public double widthFactor;
        public int capacity;
        public int maxLoad;
        public int minLoadPerStair;
        public int numLandings = 2; // TODO: take into consideration max rise before landing.
        public int numRisers;
        public int numStairsTotal;
        public int numTreads;

        /// <summary>
        /// Create equal numbers of configs
        /// </summary>
        /// <param name="input"></param>
        /// <param name="proxy"></param>
        /// <param name="proxiesWithOverrides"></param>
        /// <returns></returns>
        public StairConfig(ExitStairsInputs input, List<(ElementProxy<Elements.Floor> proxy, OccupancyOverride ovd)> proxiesWithOverrides)
        {
            // Calculate global truths
            numStairsTotal = 1 + (input.Overrides?.Additions?.Stairs?.Count ?? 0) - (input.Overrides?.Removals?.Stairs?.Count ?? 0);
            maxLoad = proxiesWithOverrides.Max(fp => fp.ovd.Value.Occupancy);
            minLoadPerStair = (int)Math.Ceiling((double)maxLoad / (double)numStairsTotal);
            maxElevationChange = GetMaxElevationChange(proxiesWithOverrides, out startElevationAtMaxElevationChange);
            extrusionHeight = proxiesWithOverrides.Max(fp => fp.proxy.Element.Elevation + maxElevationChange + 1);
            floorDepth = proxiesWithOverrides.FirstOrDefault().proxy.Element.Thickness;
            numRisers = (int)Math.Ceiling(maxElevationChange / targetRiserHeight);
            numTreads = numRisers - numLandings;
            realRiserHeight = maxElevationChange / numRisers;
            widthFactor = input.Sprinklered ? 0.2 : 0.3;
            this.calculateDimensions();
        }

        public StairConfig(StairConfig g, StairOverridesOverride ovd)
        {
            // copy props
            numStairsTotal = g.numStairsTotal;
            maxLoad = g.maxLoad;
            minLoadPerStair = g.minLoadPerStair;
            maxElevationChange = g.maxElevationChange;
            extrusionHeight = g.extrusionHeight;
            floorDepth = g.floorDepth;
            numRisers = g.numRisers;
            numTreads = g.numTreads;
            realRiserHeight = g.realRiserHeight;
            widthFactor = g.widthFactor;

            // override props
            minimumWidth = ovd.Value.MinimumTreadWidth;

            // calculate dependent props
            this.calculateDimensions();
        }

        private void calculateDimensions()
        {
            minTreadWidth = Units.InchesToMeters(widthFactor * minLoadPerStair);
            realMinTreadWidth = Math.Max(minTreadWidth, minimumWidth);
            treadWidth = realMinTreadWidth;
            capacity = (int)Math.Floor(Units.MetersToInches(treadWidth / widthFactor));
            landingDepth = realMinTreadWidth;
            realLandingDepth = Math.Max(realMinTreadWidth, 1.219); // 48 inches per 1011.6
            width = realMinTreadWidth * 2;
            firstTreads = Math.Ceiling((double)numTreads / 2);
            length = realLandingDepth * 2 + treadDepth * firstTreads;
            secondTreads = Math.Floor((double)numTreads / 2);
        }

        public static double GetMaxElevationChange(List<(ElementProxy<Elements.Floor> proxy, OccupancyOverride ovd)> proxiesWithOverrides, out double startElevation)
        {
            var maxElevationChange = 0.0;
            var ordered = proxiesWithOverrides.OrderBy(fp => fp.proxy.Element.Elevation).ToList();
            startElevation = ordered.FirstOrDefault().proxy.Element.Elevation;
            for (var i = 1; i < ordered.Count; i++)
            {
                var contender = proxiesWithOverrides[i].proxy.Element.Elevation - proxiesWithOverrides[i - 1].proxy.Element.Elevation;
                if (contender > maxElevationChange)
                {
                    startElevation = proxiesWithOverrides[i - 1].proxy.Element.Elevation;
                    maxElevationChange = contender;
                }
            }
            return maxElevationChange;
        }
    }
}