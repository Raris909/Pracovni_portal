enum WorkMode { onsite, hybrid, remote }
enum EmploymentType { fullTime, partTime, contract, internship }
enum SalaryPeriod { hour, month, year }

class JobOffer {
  final String id;
  final String companyId;
  final String companyName;
  final String title;
  final String description;
  final String requirements;
  final String? location;
  final WorkMode workMode;
  final EmploymentType employmentType;
  final double? salaryMin;
  final double? salaryMax;
  final String? salaryCurrency;
  final SalaryPeriod? salaryPeriod;
  final DateTime? publishedAt;
  final DateTime? applicationDeadline;

  const JobOffer({
    required this.id,
    required this.companyId,
    required this.companyName,
    required this.title,
    required this.description,
    required this.requirements,
    this.location,
    required this.workMode,
    required this.employmentType,
    this.salaryMin,
    this.salaryMax,
    this.salaryCurrency,
    this.salaryPeriod,
    this.publishedAt,
    this.applicationDeadline,
  });

  String get formattedSalary {
    if (salaryMin == null && salaryMax == null) return 'Mzda neuvedena';
    final min = salaryMin?.toStringAsFixed(0) ?? '';
    final max = salaryMax?.toStringAsFixed(0) ?? '';
    final range = (min.isNotEmpty && max.isNotEmpty) ? '$min – $max' : '$min$max';
    return '$range ${salaryCurrency ?? 'CZK'} / ${salaryPeriod?.name ?? 'month'}';
  }
}