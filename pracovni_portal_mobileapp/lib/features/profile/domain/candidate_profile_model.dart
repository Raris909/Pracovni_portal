class CandidateProfile {
  final String userId;
  final String email;
  final String firstName;
  final String lastName;
  final String? phone;
  final String? headline;
  final String? summary;
  final String? city;
  final String? countryCode;
  final List<String> skills;
  final String? currentCvId;
  final List<CandidateExperience> experiences;
  final List<CandidateEducation> education;
  final List<CandidateDocument> documents;

  const CandidateProfile({
    required this.userId,
    required this.email,
    required this.firstName,
    required this.lastName,
    this.phone,
    this.headline,
    this.summary,
    this.city,
    this.countryCode,
    this.skills = const [],
    this.currentCvId,
    this.experiences = const [],
    this.education = const [],
    this.documents = const [],
  });
}

class CandidateExperience {
  final String id;
  final String employer;
  final String position;
  final String? description;
  final DateTime startDate;
  final DateTime? endDate;

  const CandidateExperience({
    required this.id,
    required this.employer,
    required this.position,
    this.description,
    required this.startDate,
    this.endDate,
  });
}

class CandidateEducation {
  final String id;
  final String school;
  final String? fieldOfStudy;
  final String? degree;
  final DateTime startDate;
  final DateTime? endDate;

  const CandidateEducation({
    required this.id,
    required this.school,
    this.fieldOfStudy,
    this.degree,
    required this.startDate,
    this.endDate,
  });
}

class CandidateDocument {
  final String id;
  final String kind; 
  final String originalFilename;
  final int sizeBytes;
  final DateTime createdAt;

  const CandidateDocument({
    required this.id,
    required this.kind,
    required this.originalFilename,
    required this.sizeBytes,
    required this.createdAt,
  });
}