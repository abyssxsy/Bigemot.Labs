﻿//----------------------------------------------------------------------------
//  Copyright (C) 2004-2014 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Features2D;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Emgu.CV.Cuda;
using Emgu.CV.XFeatures2D;

namespace SURFFeatureExample
{
   public static class DrawMatches
   {
      public static void FindMatch(Image<Gray, Byte> modelImage, Image<Gray, byte> observedImage, out long matchTime, out VectorOfKeyPoint modelKeyPoints, out VectorOfKeyPoint observedKeyPoints, VectorOfVectorOfDMatch matches, out Matrix<byte> mask, out HomographyMatrix homography)
      {
         int k = 2;
         double uniquenessThreshold = 0.8;
         double hessianThresh = 300;
         
         Stopwatch watch;
         homography = null;

         modelKeyPoints = new VectorOfKeyPoint();
         observedKeyPoints = new VectorOfKeyPoint();

         
         if ( CudaInvoke.HasCuda)
         {
            CudaSURFDetector surfCuda = new CudaSURFDetector((float) hessianThresh);
            using (GpuMat gpuModelImage = new GpuMat(modelImage))
            //extract features from the object image
            using (GpuMat gpuModelKeyPoints = surfCuda.DetectKeyPointsRaw(gpuModelImage, null))
            using (GpuMat gpuModelDescriptors = surfCuda.ComputeDescriptorsRaw(gpuModelImage, null, gpuModelKeyPoints))
            using (CudaBruteForceMatcher matcher = new CudaBruteForceMatcher(DistanceType.L2))
            {
               surfCuda.DownloadKeypoints(gpuModelKeyPoints, modelKeyPoints);
               watch = Stopwatch.StartNew();

               // extract features from the observed image
               using (GpuMat gpuObservedImage = new GpuMat(observedImage))
               using (GpuMat gpuObservedKeyPoints = surfCuda.DetectKeyPointsRaw(gpuObservedImage, null))
               using (GpuMat gpuObservedDescriptors = surfCuda.ComputeDescriptorsRaw(gpuObservedImage, null, gpuObservedKeyPoints))
               //using (GpuMat tmp = new GpuMat())
               //using (Stream stream = new Stream())
               {
                  matcher.KnnMatch(gpuObservedDescriptors, gpuModelDescriptors, matches, k);

                  surfCuda.DownloadKeypoints(gpuObservedKeyPoints, observedKeyPoints);

                  mask = new Matrix<byte>(matches.Size, 1);
                  mask.SetValue(255);
                  Features2DToolbox.VoteForUniqueness(matches, uniquenessThreshold, mask);

                  int nonZeroCount = CvInvoke.CountNonZero(mask);
                  if (nonZeroCount >= 4)
                  {
                     nonZeroCount = Features2DToolbox.VoteForSizeAndOrientation(modelKeyPoints, observedKeyPoints,
                        matches, mask, 1.5, 20);
                     if (nonZeroCount >= 4)
                        homography = Features2DToolbox.GetHomographyMatrixFromMatchedFeatures(modelKeyPoints,
                           observedKeyPoints, matches, mask, 2);
                  }
               }
                  watch.Stop();
               }
            }
         
         else
        
         {
            using (UMat uModelImage = modelImage.Mat.ToUMat(AccessType.Read))
            using (UMat uObservedImage = observedImage.Mat.ToUMat(AccessType.Read))
            {
               SURFDetector surfCPU = new SURFDetector(hessianThresh);
               //extract features from the object image
               UMat modelDescriptors = new UMat();
               surfCPU.DetectAndCompute(uModelImage, null, modelKeyPoints, modelDescriptors, false);

               watch = Stopwatch.StartNew();

               // extract features from the observed image

               UMat observedDescriptors = new UMat();
               surfCPU.DetectAndCompute(uObservedImage, null, observedKeyPoints, observedDescriptors, false);
               BFMatcher matcher = new BFMatcher(DistanceType.L2);
               matcher.Add(modelDescriptors);

               matcher.KnnMatch(observedDescriptors, matches, k, null);
               mask = new Matrix<byte>(matches.Size, 1);
               mask.SetValue(255);
               Features2DToolbox.VoteForUniqueness(matches, uniquenessThreshold, mask);

               int nonZeroCount = CvInvoke.CountNonZero(mask);
               if (nonZeroCount >= 4)
               {
                  nonZeroCount = Features2DToolbox.VoteForSizeAndOrientation(modelKeyPoints, observedKeyPoints,
                     matches, mask, 1.5, 20);
                  if (nonZeroCount >= 4)
                     homography = Features2DToolbox.GetHomographyMatrixFromMatchedFeatures(modelKeyPoints,
                        observedKeyPoints, matches, mask, 2);
               }

               watch.Stop();
            }
         }
         matchTime = watch.ElapsedMilliseconds;
      }

      /// <summary>
      /// Draw the model image and observed image, the matched features and homography projection.
      /// </summary>
      /// <param name="modelImage">The model image</param>
      /// <param name="observedImage">The observed image</param>
      /// <param name="matchTime">The output total time for computing the homography matrix.</param>
      /// <returns>The model image and observed image, the matched features and homography projection.</returns>
      public static Mat Draw(Image<Gray, Byte> modelImage, Image<Gray, byte> observedImage, out long matchTime)
      {
         HomographyMatrix homography;
         VectorOfKeyPoint modelKeyPoints;
         VectorOfKeyPoint observedKeyPoints;
         using (VectorOfVectorOfDMatch matches = new VectorOfVectorOfDMatch())
         {

            Matrix<byte> mask;

            FindMatch(modelImage, observedImage, out matchTime, out modelKeyPoints, out observedKeyPoints, matches,
               out mask, out homography);

            //Draw the matched keypoints
            Mat result = new Mat();
            Features2DToolbox.DrawMatches(modelImage, modelKeyPoints, observedImage, observedKeyPoints,
               matches, result, new MCvScalar(255, 255, 255), new MCvScalar(255, 255, 255), mask);

            #region draw the projected region on the image

            if (homography != null)
            {
               //draw a rectangle along the projected model
               Rectangle rect = modelImage.ROI;
               PointF[] pts = new PointF[]
               {
                  new PointF(rect.Left, rect.Bottom),
                  new PointF(rect.Right, rect.Bottom),
                  new PointF(rect.Right, rect.Top),
                  new PointF(rect.Left, rect.Top)
               };
               homography.ProjectPoints(pts);

               Point[] points = Array.ConvertAll<PointF, Point>(pts, Point.Round);
               using (VectorOfPoint vp = new VectorOfPoint(points))
               {
                  CvInvoke.Polylines(result, vp, true, new MCvScalar(255, 0, 0, 255), 5);
               }
               //result.DrawPolyline(, true, new Bgr(Color.Red), 5);
            }

            #endregion

            return result;

         }
      }
   }
}
