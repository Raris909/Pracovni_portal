import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/jobs_repository.dart';

class JobDetailScreen extends ConsumerWidget {
  final String jobId;
  const JobDetailScreen({super.key, required this.jobId});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Scaffold(
      appBar: AppBar(title: const Text('Detail pozice')),
      body: FutureBuilder(
        future: ref.read(jobsRepositoryProvider).getJobById(jobId),
        builder: (context, snapshot) {
          if (!snapshot.hasData) {
            return const Center(child: CircularProgressIndicator());
          }
          final job = snapshot.data!;
          return SingleChildScrollView(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(job.title, style: Theme.of(context).textTheme.headlineSmall),
                const SizedBox(height: 4),
                Text(job.companyName, style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 8,
                  children: [
                    Chip(label: Text('Režim: ${job.workMode.name}')),
                    Chip(label: Text('Úvazek: ${job.employmentType.name}')),
                    Chip(label: Text(job.formattedSalary)),
                  ],
                ),
                const Divider(height: 32),
                Text('Popis pozice', style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 8),
                Text(job.description),
                const SizedBox(height: 24),
                Text('Požadavky', style: Theme.of(context).textTheme.titleMedium),
                const SizedBox(height: 8),
                Text(job.requirements),
              ],
            ),
          );
        },
      ),
      bottomNavigationBar: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: FilledButton.icon(
            icon: const Icon(Icons.send),
            label: const Text('Odpovědět na nabídku'),
            onPressed: () => _showApplyModal(context, ref),
          ),
        ),
      ),
    );
  }

  void _showApplyModal(BuildContext context, WidgetRef ref) {
    final messageCtrl = TextEditingController();
    showModalBottomSheet(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => Padding(
        padding: EdgeInsets.only(
          left: 16,
          right: 16,
          top: 24,
          bottom: MediaQuery.of(ctx).viewInsets.bottom + 24,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Přihláška na pozici', style: Theme.of(ctx).textTheme.titleLarge),
            const SizedBox(height: 16),
            TextField(
              controller: messageCtrl,
              maxLines: 4,
              decoration: const InputDecoration(
                labelText: 'Průvodní zpráva (cover_message)',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 12),
            const CheckboxListTile(
              value: true,
              onChanged: null,
              title: Text('Přiložit aktuální CV z profilu (zivotopis_2026.pdf)'),
              subtitle: Text('Sdílí snímek vašeho profilu (profile_snapshot) s firmou'),
            ),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: () async {
                await ref.read(jobsRepositoryProvider).submitApplication(
                      jobOfferId: jobId,
                      coverMessage: messageCtrl.text,
                      attachedDocumentIds: ['doc-cv-1'],
                    );
                if (ctx.mounted) {
                  Navigator.pop(ctx);
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Přihláška byla odeslána (Mock)')),
                  );
                }
              },
              child: const Text('Odeslat přihlášku'),
            ),
          ],
        ),
      ),
    );
  }
}