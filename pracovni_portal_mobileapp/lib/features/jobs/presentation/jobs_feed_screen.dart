import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../data/jobs_repository.dart';
import '../domain/job_offer_model.dart';

final jobsListProvider = FutureProvider.autoDispose<List<JobOffer>>((ref) {
  return ref.watch(jobsRepositoryProvider).getPublishedJobs();
});

class JobsFeedScreen extends ConsumerWidget {
  const JobsFeedScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final jobsAsync = ref.watch(jobsListProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Pracovní nabídky')),
      body: jobsAsync.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (err, _) => Center(child: Text('Chyba: $err')),
        data: (jobs) => ListView.separated(
          padding: const EdgeInsets.all(16),
          itemCount: jobs.length,
          separatorBuilder: (_, __) => const SizedBox(height: 12),
          itemBuilder: (context, index) {
            final job = jobs[index];
            return Card(
              child: ListTile(
                title: Text(job.title, style: const TextStyle(fontWeight: FontWeight.bold)),
                subtitle: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('${job.companyName} • ${job.location ?? "Neuvedeno"}'),
                    const SizedBox(height: 4),
                    Text(
                      job.formattedSalary,
                      style: TextStyle(color: Theme.of(context).colorScheme.primary),
                    ),
                  ],
                ),
                trailing: Chip(label: Text(job.workMode.name.toUpperCase())),
                onTap: () => context.go('/jobs/${job.id}'),
              ),
            );
          },
        ),
      ),
    );
  }
}