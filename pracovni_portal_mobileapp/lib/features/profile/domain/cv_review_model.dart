enum CvReviewStatus { queued, processing, succeeded, failed }

class CandidateCvReview {
  final String id;
  final String documentId;
  final CvReviewStatus status;
  final String model;
  final int? score; 
  final Map<String, dynamic>? result; // Parsed from jsonb
  final String? errorMessage;
  final DateTime createdAt;
  final DateTime? completedAt;

  const CandidateCvReview({
    required this.id,
    required this.documentId,
    required this.status,
    required this.model,
    this.score,
    this.result,
    this.errorMessage,
    required this.createdAt,
    this.completedAt,
  });
}