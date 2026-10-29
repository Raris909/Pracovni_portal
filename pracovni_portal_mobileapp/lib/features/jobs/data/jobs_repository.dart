import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../domain/job_offer_model.dart';

abstract class JobsRepository {
  Future<List<JobOffer>> getPublishedJobs({String? searchQuery});
  Future<JobOffer> getJobById(String id);
  Future<void> submitApplication({
    required String jobOfferId,
    required String? coverMessage,
    required List<String> attachedDocumentIds,
  });
}

class MockJobsRepository implements JobsRepository {
  final List<JobOffer> _mockJobs = [
    JobOffer(
      id: 'job-1',
      companyId: 'comp-1',
      companyName: 'TechNova s.r.o.',
      title: 'Flutter Mobile Developer',
      description: 'Hledáme vývojáře pro tvorbu multiplatformní klientské aplikace.',
      requirements: 'Flutter, Dart, REST API, Git',
      location: 'Praha',
      workMode: WorkMode.hybrid,
      employmentType: EmploymentType.fullTime,
      salaryMin: 65000,
      salaryMax: 85000,
      salaryCurrency: 'CZK',
      salaryPeriod: SalaryPeriod.month,
      publishedAt: DateTime.now().subtract(const Duration(days: 2)),
    ),
    JobOffer(
      id: 'job-2',
      companyId: 'comp-2',
      companyName: 'CloudScale a.s.',
      title: 'Junior .NET Backend Engineer',
      description: 'Vývoj mikroslužeb a REST API v ASP.NET Core a PostgreSQL.',
      requirements: 'C#, .NET 8+, SQL, Docker',
      location: 'Brno / Remote',
      workMode: WorkMode.remote,
      employmentType: EmploymentType.contract,
      salaryMin: 55000,
      salaryMax: 70000,
      salaryCurrency: 'CZK',
      salaryPeriod: SalaryPeriod.month,
      publishedAt: DateTime.now().subtract(const Duration(days: 5)),
    ),
  ];

  @override
  Future<List<JobOffer>> getPublishedJobs({String? searchQuery}) async {
    await Future.delayed(const Duration(milliseconds: 400));
    if (searchQuery == null || searchQuery.isEmpty) return _mockJobs;
    final q = searchQuery.toLowerCase();
    return _mockJobs.where((j) =>
        j.title.toLowerCase().contains(q) ||
        j.description.toLowerCase().contains(q) ||
        j.requirements.toLowerCase().contains(q)).toList();
  }

  @override
  Future<JobOffer> getJobById(String id) async {
    await Future.delayed(const Duration(milliseconds: 200));
    return _mockJobs.firstWhere((j) => j.id == id);
  }

  @override
  Future<void> submitApplication({
    required String jobOfferId,
    required String? coverMessage,
    required List<String> attachedDocumentIds,
  }) async {
    // Backend Note: Will send POST /api/applications with profile_snapshot JSON
    await Future.delayed(const Duration(milliseconds: 600));
  }
}

final jobsRepositoryProvider = Provider<JobsRepository>((ref) {
  return MockJobsRepository();
});