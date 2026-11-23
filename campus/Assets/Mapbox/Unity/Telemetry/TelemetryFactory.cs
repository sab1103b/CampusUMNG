namespace Mapbox.Unity.Telemetry
{
	public static class TelemetryFactory
	{
		public static ITelemetryLibrary GetTelemetryInstance()
		{
#if UNITY_EDITOR
			return TelemetryEditor.Instance;
#elif UNITY_IOS
			return TelemetryIos.Instance;
#elif UNITY_ANDROID
			// Campus Conecta: sin telemetría nativa en Android. Sus librerías Java (support 25.1.0) chocan con
			// androidx de Unity 6 y se excluyeron del APK; la telemetría de Mapbox es opcional.
			return TelemetryFallback.Instance;
#elif UNITY_WEBGL
			return TelemetryWebgl.Instance;
#else
			return TelemetryFallback.Instance;
#endif
		}
	}
}