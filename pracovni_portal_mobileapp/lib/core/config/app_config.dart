class AppConfig {
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5080',
  );
  static const String chatHubUrl = '$apiBaseUrl/hubs/chat';
  static const bool useMockData = true; // Flip to false when backend endpoints are ready
}