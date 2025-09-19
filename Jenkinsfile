pipeline {
  agent {
    kubernetes {
      cloud 'AKS'
      yamlFile 'deploy/pod.yaml'
    }
  }
  stages {
    stage('API') {
      parallel {
        stage('API') {
          stages {
            stage('Build API Docker Image') {
              steps {
                container('dind') {
                  sh '''
                    docker build -f deploy/Dockerfile . \
                      --build-arg PROJECT_PATH=junie-des-1942stats \
                      --build-arg PROJECT_NAME=junie-des-1942stats \
                      -t container-registry-service.container-registry:5000/bf42-stats:latest
                    docker push container-registry-service.container-registry:5000/bf42-stats:latest
                  '''
                }
              }
            }
            stage('Deploy API') {
              steps {
                container('kubectl') {
                  withKubeConfig([namespace: "bf42-stats"]) {
                     withCredentials([
                       string(credentialsId: 'bf42-stats-secrets-jwt-private-key', variable: 'JWT_PRIVATE_KEY'),
                       string(credentialsId: 'bf42-stats-secrets-refresh-token-secret', variable: 'REFRESH_TOKEN_SECRET')
                     ]) {
                       sh '''
                         set -euo pipefail
                         TMPDIR=$(mktemp -d)
                         trap 'rm -rf "$TMPDIR"' EXIT
                         printf "%s" "$JWT_PRIVATE_KEY" > "$TMPDIR/jwt-private.pem"
                         # Create or update the bf42-stats-secrets secret with both keys
                         kubectl create secret generic bf42-stats-secrets \
                           --from-file=jwt-private-key="$TMPDIR/jwt-private.pem" \
                           --from-literal=refresh-token-secret="$REFRESH_TOKEN_SECRET" \
                           --dry-run=client -o yaml | kubectl apply -f -
                       '''
                     }
                    sh 'kubectl rollout restart deployment/bf42-stats'
                  }
                }
              }
            }
          }
        }
        stage('Notifications') {
          stages {
            stage('Build Notifications Docker Image') {
              steps {
                container('dind') {
                  sh '''
                    docker build -f deploy/Dockerfile . \
                      --build-arg PROJECT_PATH=junie-des-1942stats.Notifications \
                      --build-arg PROJECT_NAME=junie-des-1942stats.Notifications \
                      -t container-registry-service.container-registry:5000/bf42-notifications:latest
                    docker push container-registry-service.container-registry:5000/bf42-notifications:latest
                  '''
                }
              }
            }
            stage('Deploy Notifications') {
              steps {
                container('kubectl') {
                  withKubeConfig([namespace: "bf42-stats"]) {
                    sh 'kubectl rollout restart deployment/bf42-notifications'
                  }
                }
              }
            }
          }
        }
      }
    }
  }
}