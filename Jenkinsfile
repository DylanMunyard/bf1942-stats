pipeline {
  agent {
    kubernetes {
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