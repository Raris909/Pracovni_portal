import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../../shared/widgets/main_scaffold.dart';
import '../../features/auth/presentation/login_screen.dart';
import '../../features/jobs/presentation/jobs_feed_screen.dart';
import '../../features/jobs/presentation/job_detail_screen.dart';
import '../../features/applications/presentation/my_applications_screen.dart';
import '../../features/chat/presentation/conversations_screen.dart';
import '../../features/profile/presentation/profile_screen.dart';
import '../../features/profile/presentation/cv_ai_review_screen.dart';

final _rootNavigatorKey = GlobalKey<NavigatorState>();

final appRouterProvider = Provider<GoRouter>((ref) {
  return GoRouter(
    navigatorKey: _rootNavigatorKey,
    initialLocation: '/jobs',
    routes: [
      GoRoute(
        path: '/login',
        builder: (context, state) => const LoginScreen(),
      ),
      StatefulShellRoute.indexedStack(
        builder: (context, state, navigationShell) {
          return MainScaffold(navigationShell: navigationShell);
        },
        branches: [
          // 1. Jobs Tab
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/jobs',
                builder: (context, state) => const JobsFeedScreen(),
                routes: [
                  GoRoute(
                    path: ':id',
                    builder: (context, state) => JobDetailScreen(
                      jobId: state.pathParameters['id']!,
                    ),
                  ),
                ],
              ),
            ],
          ),
          // 2. Applications & Recruitment Stages Tab
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/applications',
                builder: (context, state) => const MyApplicationsScreen(),
              ),
            ],
          ),
          // 3. Chat Tab
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/chat',
                builder: (context, state) => const ConversationsScreen(),
              ),
            ],
          ),
          // 4. Candidate Profile & AI CV Review Tab
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/profile',
                builder: (context, state) => const ProfileScreen(),
                routes: [
                  GoRoute(
                    path: 'cv-review',
                    builder: (context, state) => const CvAiReviewScreen(),
                  ),
                ],
              ),
            ],
          ),
        ],
      ),
    ],
  );
});