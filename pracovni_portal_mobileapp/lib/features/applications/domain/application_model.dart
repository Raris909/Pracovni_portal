enum ApplicationStatus { submitted, inProgress, rejected, accepted, withdrawn }
enum StageStatus { pending, inProgress, passed, failed, skipped }

class JobApplication {
  final String id;
  final String jobOfferId;
  final String jobTitle;
  final String companyName;
  final ApplicationStatus status;
  final String? coverMessage;
  final DateTime submittedAt;
  final List<ApplicationStageItem> stages;

  const JobApplication({
    required this.id,
    required this.jobOfferId,
    required this.jobTitle,
    required this.companyName,
    required this.status,
    this.coverMessage,
    required this.submittedAt,
    this.stages = const [],
  });
}

class ApplicationStageItem {
  final String stageId;
  final String name;
  final String kind; 
  final int position;
  final StageStatus status;
  final List<InterviewSlot> availableSlots;
  final InterviewSlot? bookedSlot;

  const ApplicationStageItem({
    required this.stageId,
    required this.name,
    required this.kind,
    required this.position,
    required this.status,
    this.availableSlots = const [],
    this.bookedSlot,
  });
}

class InterviewSlot {
  final String id;
  final DateTime startsAt;
  final DateTime endsAt;
  final String? location;
  final String? meetingUrl;

  const InterviewSlot({
    required this.id,
    required this.startsAt,
    required this.endsAt,
    this.location,
    this.meetingUrl,
  });
}