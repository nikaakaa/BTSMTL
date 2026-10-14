using System;

using BEPUutilities;
using FixMath.NET;

namespace BEPUphysics.CollisionShapes.ConvexShapes
{
    ///<summary>
    /// Ball-like shape.
    ///</summary>
    public class SphereShape : ConvexShape
    {

        //This is a convenience method.  People expect to see a 'radius' of some kind.
        ///<summary>
        /// Gets or sets the radius of the sphere.
        ///</summary>
        public Fix64 Radius { get { return collisionMargin; } set { CollisionMargin = value; } }

        ///<summary>
        /// Constructs a new sphere shape.
        ///</summary>
        ///<param name="radius">Radius of the sphere.</param>
        public SphereShape(Fix64 radius)
        {
            Radius = radius;

            UpdateConvexShapeInfo(ComputeDescription(radius));
        }


        ///<summary>
        /// Constructs a new sphere shape.
        ///</summary>
        /// <param name="description">Cached information about the shape. Assumed to be correct; no extra processing or validation is performed.</param>
        public SphereShape(ConvexShapeDescription description)
        {
            UpdateConvexShapeInfo(description);
        }

        protected override void OnShapeChanged()
        {
            UpdateConvexShapeInfo(ComputeDescription(Radius));
            base.OnShapeChanged();
        }

        /// <summary>
        /// Computes a convex shape description for a SphereShape.
        /// </summary>
        ///<param name="radius">Radius of the sphere.</param>
        /// <returns>Description required to define a convex shape.</returns>
        public static ConvexShapeDescription ComputeDescription(Fix64 radius)
        {
            ConvexShapeDescription description;
            description.EntityShapeVolume.Volume = F64.FourThirds * MathHelper.Pi * radius * radius * radius;
            description.EntityShapeVolume.VolumeDistribution = new Matrix3x3();
            Fix64 diagValue = ((F64.TwoFifths) * radius * radius);
            description.EntityShapeVolume.VolumeDistribution.M11 = diagValue;
            description.EntityShapeVolume.VolumeDistribution.M22 = diagValue;
            description.EntityShapeVolume.VolumeDistribution.M33 = diagValue;

            description.MinimumRadius = radius;
            description.MaximumRadius = radius;

            description.CollisionMargin = radius;
            return description;
        }


        /// <summary>
        /// Gets the bounding box of the shape given a transform.
        /// </summary>
        /// <param name="shapeTransform">Transform to use.</param>
        /// <param name="boundingBox">Bounding box of the transformed shape.</param>
        public override void GetBoundingBox(ref RigidTransform shapeTransform, out BoundingBox boundingBox)
        {
#if !WINDOWS
            boundingBox = new BoundingBox();
#endif
            boundingBox.Min.X = shapeTransform.Position.X - collisionMargin;
            boundingBox.Min.Y = shapeTransform.Position.Y - collisionMargin;
            boundingBox.Min.Z = shapeTransform.Position.Z - collisionMargin;
            boundingBox.Max.X = shapeTransform.Position.X + collisionMargin;
            boundingBox.Max.Y = shapeTransform.Position.Y + collisionMargin;
            boundingBox.Max.Z = shapeTransform.Position.Z + collisionMargin;
        }


        //TODO: Could do a little optimizing.  If the methods were virtual, could override and save a conjugate/transform.
        ///<summary>
        /// Gets the extreme point of the shape in local space in a given direction.
        ///</summary>
        ///<param name="direction">Direction to find the extreme point in.</param>
        ///<param name="extremePoint">Extreme point on the shape.</param>
        public override void GetLocalExtremePointWithoutMargin(ref Vector3 direction, out Vector3 extremePoint)
        {
            extremePoint = Toolbox.ZeroVector;
        }

    }
}
