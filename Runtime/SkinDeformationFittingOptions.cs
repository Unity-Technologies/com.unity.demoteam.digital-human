namespace Unity.DemoTeam.DigitalHuman
{
	public static class SkinDeformationFittingOptions
	{
		public enum Method
		{
			LinearLeastSquares,
			NonNegativeLeastSquares,
		}

		public enum Param
		{
			DeltaPosition,
			OutputEdgeLength,
			OutputEdgeCurvature,
		}
	}
}
