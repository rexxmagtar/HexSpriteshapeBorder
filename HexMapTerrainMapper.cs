using System;
using System.Collections.Generic;
using ca.axoninteractive.Geometry.Hex;
using com.pigsels.tools;
using UnityEngine;
using UnityEngine.U2D;
using Debug = UnityEngine.Debug;

namespace com.pigsels.BubbleTrouble
{
    /// <summary>
    /// Class maps Hex grid <see cref="HexGrid"/> and SpriteShape <see cref="SpriteShapeController"/> curve. Used for calculating intersections between hex grid and curve.
    /// Does only math stuff without changing anything.
    /// </summary>
    public static class HexMapTerrainMapper
    {
        /// <summary>
        /// Gets walls for all spriteshapes on current scene. Covers all spriteshapes at the Scene.
        /// </summary>
        /// <param name="hexMap">Hex grid map</param>
        /// <param name="radiusWeight">The weight of inscribed circle radius. Used during intersection check. Instead of hex intersections check we check circle intersections,
        /// the less radius can be provided to reduce number if intersections with curve (size matters), subsequently allowing hexes to be partially covered by sprite shape curve.</param>
        /// <param name="limitBounds">If this rect is specified, only points of spriteshapes withing this rect will be considered.</param>
        /// <returns>The list of hex coordinates in space of specified hex map</returns>
        public static List<CubicHexCoord> GetAllSpriteShapeWalls(HexMap hexMap, float radiusWeight = 1, Rect? limitBounds = null)
        {
            //SpriteShapeController[] shapeControllers = GameObject.FindObjectsOfType<SpriteShapeController>();
            var shapeControllers = HelperTools.GetSceneBehaviors<SpriteShapeController>(false);

            List<CubicHexCoord> result = new List<CubicHexCoord>();

            foreach (var shapeController in shapeControllers)
            {
                Vector2 leftPoint = shapeController.transform.TransformPoint(shapeController.edgeCollider.points[0]);
                Vector2 rightPoint = shapeController.transform.TransformPoint(shapeController.edgeCollider.points[shapeController.edgeCollider.points.Length - 2]);

                result.AddRange(GetWallHex(hexMap, shapeController, leftPoint, rightPoint, radiusWeight, limitBounds));
            }

            return result;
        }

        /// <summary>
        /// Gets walls for specified sprite shape region restricted by 2 spline pivot points. For example if point <see cref="pointNumber"/> == 3, then the walls will calculated for a path from point 2 to point 4.
        /// </summary>
        /// <param name="shapeController">Specified sprite shape controller</param>
        /// <param name="hexMap"></param>
        /// <param name="pointNumber">The number of pivot point of spline. This point and his to relatives (left and right) determine the region the walls will be calculated for</param>
        /// <param name="radiusWeight">The weight of inscribed circle radius. Used during intersection check. Instead of hex intersections check we check circle intersections,
        /// the less radius can be provided to reduce number if intersections with curve (size matters), subsequently allowing hexes to be partially covered by sprite shape curve.</param>
        /// <returns></returns>
        public static HashSet<CubicHexCoord> GetWallsForSpriteShapePointBorders(SpriteShapeController shapeController, HexMap hexMap, int pointNumber, float radiusWeight = 1)
        {
            Vector2 leftPoint = shapeController.transform.TransformPoint(shapeController.spline.GetPosition(pointNumber > 0 ? (pointNumber - 1) : shapeController.spline.GetPointCount() - 1));

            Vector2 rightPoint = shapeController.transform.TransformPoint(shapeController.spline.GetPosition((pointNumber + 1) % shapeController.spline.GetPointCount()));

            return GetWallHex(hexMap, shapeController, leftPoint, rightPoint, radiusWeight);
        }

        /// <summary>
        /// Gets walls for specified SpriteShape region restricted by 2 spline points.
        /// </summary>
        /// <param name="hexMap"></param>
        /// <param name="shapeController">Specified SpriteShapeController</param>
        /// <param name="startPoint">First SpriteShape point</param>
        /// <param name="endPoint">Last SpriteShape point</param>
        /// <param name="radiusWeight">The weight of inscribed circle radius. Used during intersection check. The radius of a circle inscribed in hex is multiplied by this value before intersection check. Instead of hex intersections check we check circle intersections,
        /// the less radius can be provided to reduce number if intersections with curve (size matters), subsequently allowing hexes to be partially covered by sprite shape curve.</param>
        /// <param name="limitBounds">If this rect is specified, only points of <paramref name="shapeController"/> withing this rect will be considered.</param>
        /// <returns></returns>
        public static HashSet<CubicHexCoord> GetWallHex(HexMap hexMap,
            SpriteShapeController shapeController,
            Vector2 startPoint,
            Vector2 endPoint,
            float radiusWeight = 1,
            Rect? limitBounds = null)
        {
            var hexes = new HashSet<CubicHexCoord>();
            Vector2[] shapePoints = shapeController.edgeCollider.points;
            Vector2[] shapePointsTemp = new Vector2[shapePoints.Length];

            //Converting edge collider points to world space.
            for (int i = 0; i < shapePoints.Length; i++)
            {
                shapePointsTemp[i] = shapeController.gameObject.transform.TransformPoint(shapePoints[i]);
            }
            shapePoints = shapePointsTemp;

            //Getting first and last points from edge collider. 
            int startIndex = FindClosestPoint(shapePoints, startPoint);
            int endIndex = FindClosestPoint(shapePoints, endPoint);

            float circleRadius = hexMap.hexInscribedCircleRadius * radiusWeight;

            bool isCheckBounds = limitBounds != null;
            Rect boundsRect = isCheckBounds ? (Rect)limitBounds : new Rect();

            //Debug.Log($"startIndex={startIndex}, endIndex={endIndex}");

            //Looping through all edge collider segments. A segment is described with two relative points of shapePoints array.
            for (int i = startIndex; i != endIndex; i = (i + 1) % shapePoints.Length)
            {
                Vector2 linePointA = shapePoints[i];
                Vector2 linePointB = shapePoints[(i + 1) % shapePoints.Length];
                Vector2 lineDirection = (linePointB - linePointA).normalized;

                //Checking if current segment lies completely in rect.
                //So even if a segment intersects rect but its border points do not lie in the rect then the check returns false.
                //This is not a problem because rect is big enough and the extreme points cannot cause a lot of problems because they aren't even seen by the player.
                //This check is also much cheaper then the full one.
                if (isCheckBounds && !boundsRect.Contains(linePointA) && !boundsRect.Contains(linePointB))
                {
                    continue;
                }

                //Inside each segment looping through all hexes that this segment intersects.
                for (Vector2 x = linePointA; (x - linePointB).sqrMagnitude > 0.001f;)
                {
                    //If current iteration point x is no longer inside segment then it means that we have checked all points inside the segment.
                    //The cycle ends.
                    if (!HexMap.IsCBetweenAB(linePointA, linePointB, x))
                    {
                        break;
                    }

                    //Getting center of hex that current iteration point belongs to.
                    Vector2 hexCenter = hexMap.AdjustPointPosition(x);

                    CubicHexCoord hex = hexMap.PointToCubic(hexCenter);

                    //To iterate through all hex that intersect with segment we jump from one hex border to another hex border.
                    //To get border coordinates we need to find an intersection between a segment and a hex.
                    List<Vector2> intersectionPoints = hexMap.GetIntersectionPoints(hex, x, linePointB);

                    //To check if we need to block current intersected Hex (set wall inside this hex) we need to check an intersection with inscribed circle (inscribed in hex).
                    List<Vector2> intersectionPointsWithCircle = hexMap.GetIntersectionPointsUsingRound(hex, x, linePointB, radiusWeight);

                    //Checking if any intersection between segment and circle occured. Separately checking the case where segment fully lies inside circle without any intersections. 
                    if ((intersectionPointsWithCircle != null && intersectionPointsWithCircle.Count != 0)
                        || HexMap.IsSegmentInsideCircle(hexCenter, circleRadius, x, linePointB))
                    {
                        hexes.Add(hex);
                    }

                    //If there is found any intersection then we jump from current iteration point to the next intersection.
                    //If there are no intersection then work with current segment is done.
                    if (intersectionPoints != null && intersectionPoints.Count != 0)
                    {
                        Vector2 extraLength = lineDirection * 0.01f;

                        //If there are 2 intersections then the closest one to segment end point will be chosen (the farthest one from iteration point).
                        if (intersectionPoints.Count == 1 || (intersectionPoints[0] - x).sqrMagnitude > (intersectionPoints[1] - x).sqrMagnitude)
                        {
                            x = intersectionPoints[0];
                        }
                        else
                        {
                            x = intersectionPoints[1];
                        }

                        //Increasing x by some small extra value to move a bit away from hex border to exclude the possibility to jump again to previous hex.
                        x += extraLength;
                    }
                    else
                    {
                        x = shapePoints[i + 1];
                    }
                }
            }

            //Adding one extra layer of walls in terms of elimitation case in which bubble tries to move out from wall hex to a different hex which is inside terrain shape.
            //Basically, the wall becomes thiker
            HashSet<CubicHexCoord> extraLayerCoords = new HashSet<CubicHexCoord>();

            foreach (var cubicHexCoord in hexes)
            {
                CubicHexCoord[] relativeHexes = cubicHexCoord.RingAround(1);

                for (int i = 0; i < relativeHexes.Length; i++)
                {
                    Vector2 hexCenterPoint = hexMap.CubicToPoint(relativeHexes[i]);

                    if (HexMapTerrainMapper.IsPointInsideCollider(shapeController.edgeCollider, hexCenterPoint))
                    {
                        extraLayerCoords.Add(relativeHexes[i]);
                    }
                }
            }

            foreach (var extraCubicHexCoord in extraLayerCoords)
            {
                hexes.Add(extraCubicHexCoord);
            }

            return hexes;
        }

        /// <summary>
        /// Iterative algorithm that checks if a given point is inside a specified close-ended collider.
        /// This algorithm still can provide wrong answer in rare cases due to percision loss
        /// (e.g. when the colliser polygon has a thin spike that might be jumped over by the ray tracer or the ray comes through the vertex of the surface).
        /// The related discussion: <see href="https://answers.unity.com/questions/163864/test-if-point-is-in-collider.html"/>
        /// </summary>
        /// <param name="collider"></param>
        /// <param name="point"></param>
        /// <returns>True if point is inside collider. False otherwise.</returns>
        public static bool IsPointInsideCollider(Collider2D collider, Vector2 point)
        {
            //Basically we are going to "ray" our way to an end point from given point and count intersections with given collider.
            //In case intersections count is odd the point is inside collider. Outside over-wise 

            int intersectionsCount = 0;

            //Any end point far away from collider can be chosen.
            //To minimize number of "magic numbers" in code, left bottom corner of collider bounding box is used as an end point.
            //Small offset from corner is also done to prevent accuracy errors.
            Vector2 endPoint = collider.bounds.min - new Vector3(1,1);

            var direction = (endPoint - point).normalized;

            while (true)
            {

                bool foundCollider = false;

                var hits = Physics2D.RaycastAll(point, direction);

                RaycastHit2D hitRequiredCollider = new RaycastHit2D();

                //Searching for needed collider.
                foreach (var hit in hits)
                {
                    if (hit.collider == collider)
                    {
                        hitRequiredCollider = hit;

                        foundCollider = true;

                        break;
                    }
                }

                //No more intersections will be possible. Rays has moved out from  collider.
                if (!foundCollider)
                {
                    return intersectionsCount % 2 == 1;
                }
                else
                {
                    //Due to possible precision loss a raycast may hit the same point infinite number of times.
                    //To prevent this small offset is added to next rays origins point. 
                    if (point == hitRequiredCollider.point)
                    {
                        point += direction / 100f;
                        continue;
                    }

                    //Moving to next point. Doing small offset in terms not to hit the same point twice.
                    point = hitRequiredCollider.point + direction / 100f;

                    //In some extremely rare cases ray can not enter but just touch the surface.
                    //Subsequently intersections counter must not be changed because we need to count only enters and exits.
                    if (Math.Abs(Vector2.Dot(hitRequiredCollider.normal, direction)) < 0.001f) // Checking if ray and touched surface normal are perpendicular.
                    {
                        continue;
                    }

                    intersectionsCount++;
                }
            }
        }

        /// <summary>
        /// Find a point closest to the specified <paramref name="referencePoint"/> point from <see cref="points"/>.
        /// </summary>
        /// <param name="points">Array of points to look up.</param>
        /// <param name="referencePoint">Point to search closest point to.</param>
        /// <returns>The index of closest point in array. -1 if no point was found.</returns>
        public static int FindClosestPoint(Vector2[] points, Vector2 referencePoint)
        {
            float minMagnitude = float.MaxValue;

            int minIndex = -1;
            for (int i = 0;
                i < points.Length;
                i++)
            {
                float distance = (points[i] - referencePoint).sqrMagnitude;

                if (distance < minMagnitude)
                {
                    minIndex = i;
                    minMagnitude = distance;
                }
            }
            return minIndex;
        }
    }
}