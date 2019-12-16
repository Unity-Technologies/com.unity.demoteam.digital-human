namespace Unity.DemoTeam.DigitalHuman
{
	public static class SkinDeformationFitting
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
